using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers;

// The enchant transfer dialog (inventory menu): the client sends the uid of one gear item whose id, equip slot,
// enchant level, rarity and required level match a row of itemenchanttransform.xml. The item and the row's meso
// are taken and the row's output items given; the client then gets the empty result packet.
public class TransferEnchantHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.TransferEnchant;

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required TableMetadataStorage TableMetadata { private get; init; }
    // ReSharper restore All
    #endregion

    public override void Handle(GameSession session, IByteReader packet) {
        long itemUid = packet.ReadLong();

        lock (session.Item) {
            Item? item = session.Item.Inventory.Get(itemUid);
            if (item == null) {
                return;
            }

            // CUIItemEnchantTransformDialog::CanTransform: a locked item, or one waiting out its unlock time, is refused.
            if (item.IsLocked || item.UnlockTime > DateTimeOffset.UtcNow.ToUnixTimeSeconds()) {
                return;
            }

            ItemEnchantTransformTable.Entry? entry = FindEntry(item);
            if (entry == null) {
                return;
            }

            // The client checks the meso before it sends.
            if (session.Currency.Meso < entry.NeedMeso) {
                return;
            }

            var outputs = new List<Item>();
            foreach (ItemEnchantTransformTable.Output output in entry.Outputs) {
                Item? created = session.Field?.ItemDrop.CreateItem(output.ItemId, output.Rarity, output.Amount);
                if (created == null) {
                    Logger.Warning("TransferEnchant output {ItemId} has no item metadata", output.ItemId);
                    return;
                }
                outputs.Add(created);
            }

            // Every output needs its own slot; the input's slot only frees up in the gear tab.
            foreach (IGrouping<InventoryType, Item> group in outputs.GroupBy(output => output.Inventory)) {
                int free = session.Item.Inventory.FreeSlots(group.Key) + (group.Key == item.Inventory ? 1 : 0);
                if (free < group.Count()) {
                    return;
                }
            }

            if (!session.Item.Inventory.Consume(item.Uid)) {
                return;
            }
            session.Currency.Meso -= entry.NeedMeso;

            using GameStorage.Request db = session.GameStorage.Context();
            foreach (Item output in outputs) {
                Item? saved = db.CreateItem(0, output);
                if (saved == null) {
                    throw new InvalidOperationException($"Failed to create transfer output item: {output.Id}");
                }
                session.Item.Inventory.Add(saved, true);
            }

            session.Send(TransferEnchantPacket.Result());
        }
    }

    private ItemEnchantTransformTable.Entry? FindEntry(Item item) {
        int enchantLevel = item.Enchant?.Enchants ?? 0;
        foreach (ItemEnchantTransformTable.Entry entry in TableMetadata.ItemEnchantTransformTable.Entries) {
            if (entry.Rarity == item.Rarity
                    && entry.LimitLevel == item.Metadata.Limit.Level
                    && entry.EnchantLevel == enchantLevel
                    && Array.IndexOf(entry.Slots, (int) item.Type.Type) >= 0
                    && Array.IndexOf(entry.ItemIds, item.Id) >= 0) {
                return entry;
            }
        }
        return null;
    }
}
