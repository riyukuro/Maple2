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

public class ItemExtractionHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.ItemExtractionScroll;

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required TableMetadataStorage TableMetadata { private get; init; }
    // ReSharper restore All
    #endregion

    public override void Handle(GameSession session, IByteReader packet) {
        long anvilItemUid = packet.ReadLong();
        long sourceItemUid = packet.ReadLong();

        lock (session.Item) {
            Item? anvil = session.Item.Inventory.Get(anvilItemUid);
            Item? sourceItem = session.Item.Inventory.Get(sourceItemUid);

            // Every refusal is answered: the dialog leaves its waiting state on any reply (vfunc_79 calls vfunc_67
            // after the result, uiitemextractionscrolldialog.cpp:273). A missing anvil gets the lack-of-anvils notice.
            if (anvil?.Metadata.Property.Tag != ItemTag.ItemExtraction) {
                session.Send(ItemExtractionPacket.InsufficientAnvils());
                return;
            }
            if (sourceItem is not { GlamorForges: > 0 }
                || !TableMetadata.ItemExtractionTable.Entries.TryGetValue(sourceItem.Id, out ItemExtractionTable.Entry? entry)) {
                session.Send(ItemExtractionPacket.Refused());
                return;
            }

            Item? resultItem = session.Field?.ItemDrop.CreateItem(entry.ResultItemId);
            if (resultItem == null) {
                session.Send(ItemExtractionPacket.Refused());
                return;
            }
            MarkExtracted(resultItem);
            CopyAppearance(sourceItem, resultItem);

            // The result goes to its own tab (an outfit for a skin), not always to gear.
            if (!session.Item.Inventory.CanAdd(resultItem, resultItem.Amount)) {
                session.Send(ItemExtractionPacket.FullInventory());
                return;
            }

            var anvils = new IngredientInfo(ItemTag.ItemExtraction, entry.ScrollCount);
            if (!session.Item.Inventory.Consume([anvils])) {
                session.Send(ItemExtractionPacket.InsufficientAnvils());
                return;
            }

            using GameStorage.Request db = session.GameStorage.Context();
            resultItem = db.CreateItem(0, resultItem);
            if (resultItem == null) {
                throw new InvalidOperationException($"Failed to create result item: {entry.ResultItemId}");
            }

            session.Item.Inventory.Add(resultItem, true);
            sourceItem.GlamorForges--;

            session.Send(ItemExtractionPacket.Extract(sourceItem.Uid, sourceItem.GlamorForges, resultItem));
        }
    }

    // The result takes the source's appearance, its dye with it: the preview copies the source's extra data into the
    // result (spScroll->GetProxy()->vfunc_3(spItem->GetProxy()), uiitemextractionscrolldialog.cpp:353; the proxy is
    // the CItemExtraData of CItemData::CreateExtraData, itemdata.cpp:1430, the server's ItemAppearance). An
    // appearance of another kind gets the colour only (chosen here; every
    // extraction row pairs items of one item type).
    internal static void CopyAppearance(Item source, Item result) {
        if (source.Appearance == null) {
            return;
        }
        if (result.Appearance == null || result.Appearance.GetType() == source.Appearance.GetType()) {
            result.Appearance = source.Appearance.Clone();
            return;
        }
        result.Appearance.Color = source.Appearance.Color;
    }

    // The client shows the result as it will be (UIItemExtractionScrollDialog, the preview of the selected item): its
    // CItemTransfer gets SetSocketTransferFlag(false) (m_b42, the sockets may not move) and FUN_140635300 (m_b3F, it
    // may not be dismantled).
    internal static void MarkExtracted(Item item) {
        if (item.Transfer == null) {
            return;
        }
        item.Transfer.SocketTransfer = false;
        item.Transfer.DisableBreak = true;
    }
}
