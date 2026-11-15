using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Game.PacketHandlers;

// The enchant transform window (inventory menu, UIItemEnchantTransformDialog::OnSubmitTransform): the client sends
// the uid of one gear-tab item whose id, equip slot, enchant level and rarity match a row of itemenchanttransform.xml
// (EnchantTransform.Find). The item and the row's meso are taken and the row's output items given; the client then
// gets the empty result packet. A refused request changes nothing and gets no answer.
public class TransferEnchantHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.TransferEnchant;

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required TableMetadataStorage TableMetadata { private get; init; }
    // ReSharper restore All
    #endregion

    // The only sender is the transform dialog (uiitemenchanttransformdialog.cpp:350). With EnchantTransform01 off the
    // inventory hides the button that opens it (UIInventoryDialog, uiinventorydialog.cpp:1008). An npc script function
    // whose uiName is ItemEnchantTransformDialog can still open it (NpcTalk OpenDialog), as the dialog checks no feature;
    // its submit is then dropped here, like the other silent refusals.
    public const string Feature = "EnchantTransform01";

    public override void Handle(GameSession session, IByteReader packet) {
        long itemUid = packet.ReadLong();
        if (!session.ServerTableMetadata.FeatureTable.IsOn(Feature)) {
            return;
        }

        lock (session.Item) {
            Item? item = session.Item.Inventory.Get(itemUid);
            if (item == null) {
                return;
            }

            // CItemEnchantTransformTable::CanTransform: a row for the item, not locked or waiting out its unlock time.
            if (EnchantTransform.Locked(item.IsLocked, item.UnlockTime, DateTimeOffset.UtcNow.ToUnixTimeSeconds())) {
                return;
            }

            ItemEnchantTransformTable.Entry? entry = EnchantTransform.Find(TableMetadata.ItemEnchantTransformTable,
                item.Id, (int) item.Type.Type, item.Enchant?.Enchants ?? 0, item.Rarity);
            if (entry == null) {
                return;
            }

            // The client checks the meso before it sends. The mesos are held here without a packet (the input's
            // consume sends one, and a spend before the old take made it throw with the input gone) and given back on
            // each refusal below; the packet goes where the take was.
            if (!session.Currency.TryTakeMeso(entry.NeedMeso, notify: false)) {
                return;
            }

            var outputs = new List<Item>();
            foreach (ItemEnchantTransformTable.Output output in entry.Outputs) {
                Item? created = session.Field?.ItemDrop.CreateItem(output.ItemId, output.Rarity, output.Amount);
                if (created == null) {
                    Logger.Warning("TransferEnchant output {ItemId} has no item metadata", output.ItemId);
                    session.Currency.ReturnMeso(entry.NeedMeso, notify: false);
                    return;
                }
                outputs.Add(created);
            }

            // Every output needs its own slot; the input's slot only frees up in the gear tab.
            foreach (IGrouping<InventoryType, Item> group in outputs.GroupBy(output => output.Inventory)) {
                int free = session.Item.Inventory.FreeSlots(group.Key) + (group.Key == item.Inventory ? 1 : 0);
                if (free < group.Count()) {
                    session.Currency.ReturnMeso(entry.NeedMeso, notify: false);
                    return;
                }
            }

            // The input and the meso are stored as taken in one save before the outputs are given (their rows are
            // made right after): taken in memory only, a crash before the next session save kept the outputs and gave
            // the input and the meso back, so the transform could be made again.
            if (!session.Item.Inventory.ConsumeNow([(item.Uid, item.Amount)])) {
                session.Currency.ReturnMeso(entry.NeedMeso, notify: false);
                return;
            }
            session.Currency.NotifyMeso();

            // Each output gets its row from the bag's add (the character's), or from the mail when the bag's write
            // fails. Before, each row was made first with no owner and a failed write threw after the input and the
            // meso were taken: that output and the ones after it were lost, and no result was sent. An output neither
            // the bag nor the mail takes is logged (chosen here: the transform stands, as for other rewards).
            foreach (Item output in outputs) {
                if (!session.Item.AddOrMail(output, notifyNew: true)) {
                    Logger.Error("TransferEnchant output {ItemId} x{Amount} of {CharacterId} neither added nor mailed",
                        output.Id, output.Amount, session.CharacterId);
                }
            }

            session.Send(TransferEnchantPacket.Result());
        }
    }
}
