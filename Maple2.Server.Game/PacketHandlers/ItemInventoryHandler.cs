using Maple2.Model;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers;

public class ItemInventoryHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.RequestItemInventory;

    private enum Command : byte {
        Move = 3,
        Drop = 4,
        DropAll = 5,
        // KMS 2525 client: UIInventoryDialog::SendRequestItemInventory (8, tab, flag) and
        // Unk_Inventory::SendRequestItemInventory (9, tab) for the expand button.
        Sort = 8,
        Expand = 9,
    }

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        switch (command) {
            case Command.Move:
                HandleMove(session, packet);
                return;
            case Command.Drop:
                HandleDrop(session, packet);
                return;
            case Command.DropAll:
                HandleDropAll(session, packet);
                return;
            case Command.Sort:
                HandleSort(session, packet);
                return;
            case Command.Expand:
                HandleExpand(session, packet);
                return;
        }
    }

    private void HandleMove(GameSession session, IByteReader packet) {
        long uid = packet.ReadLong();
        short dstSlot = packet.ReadShort();

        session.Item.Inventory.Move(uid, dstSlot);
    }

    private void HandleDrop(GameSession session, IByteReader packet) {
        if (session.Field == null) {
            return;
        }

        long uid = packet.ReadLong();
        int amount = packet.ReadInt();
        // The client sends 1 to the stack's count; 0 and below fell through to the whole stack.
        if (amount <= 0) {
            return;
        }
        DropItem(session, uid, amount);
    }

    private void HandleDropAll(GameSession session, IByteReader packet) {
        if (session.Field == null) {
            return;
        }

        long uid = packet.ReadLong();
        DropItem(session, uid);
    }

    private void HandleSort(GameSession session, IByteReader packet) {
        byte tab = packet.ReadByte();
        // The client asks this in a confirm box it shows only when the tab holds expired items
        // (UIInventoryDialog::OnClickSort counts IsExpired items, OnSortConfirm sends the answer), so it asks to
        // remove the expired items. The client has no choice for merging, so a sort always merges stacks.
        // The counting helper at 0x14199d840 is named CountStackableItems in the decompiled client, but its body
        // counts CItem::IsExpired; with no expired item the client skips the box and sends false.
        bool removeExpired = packet.ReadBool();
        if (!InventoryTab.TryFromClient(tab, out InventoryType type)) {
            return;
        }

        session.Item.Inventory.Sort(type, removeExpired: removeExpired, mergeStacks: true);
    }

    private void HandleExpand(GameSession session, IByteReader packet) {
        if (!InventoryTab.TryFromClient(packet.ReadByte(), out InventoryType type)) {
            return;
        }

        session.Item.Inventory.Expand(type);
    }

    /// <summary>
    /// Drop (mode 4) and destroy (mode 5). The client sends mode 4 with the count it chose only for an item it may
    /// drop (CItemTransfer::CanDestroy: trade bit, no binding, not locked; no gem; InvenSlot::vfunc_26), and mode 5
    /// from the destroy box it shows for every other item (UIMessageBox::HandleDestroyItemButton).
    /// </summary>
    /// <param name="amount">The amount to drop; -1 destroys the whole item.</param>
    private void DropItem(GameSession session, long uid, int amount = -1) {
        if (session.Field is null) return;
        Item? drop = session.Item.Inventory.Get(uid);
        if (drop == null) {
            return;
        }

        // The client refuses these itself and sends nothing.
        if (!ItemLockRules.CanDrop(session.Player.IsDead,
                ItemLockRules.IsUsable(drop.IsLocked, drop.UnlockTime, DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
                uid, session.SummonedPetUid, session.Instrument?.Score?.Uid ?? 0)) {
            return;
        }
        // Nor an item with a gemstone in one of its sockets (CItemSocket FUN_140622750; the client shows
        // s_itembreak_usedgemstone_item and sends nothing).
        if (drop.Socket?.Sockets.Any(gem => gem != null && gem.ItemId != 0) == true) {
            return;
        }

        if (drop.Metadata.Property.DisableDrop) {
            session.Send(NoticePacket.Notice(NoticePacket.Flags.MessageBox, StringCode.s_item_err_drop));
            return;
        }

        bool toField = amount > 0 && IsFieldDrop(drop);
        if (!session.Item.Inventory.Remove(uid, out drop, toField ? amount : -1)) {
            return;
        }

        if (!toField) {
            session.Item.Inventory.Discard(drop);
            return;
        }

        session.Field.DropItem(session.Player, drop);
    }

    // The item lands on the field (CItemTransfer::CanDestroy); anything else is destroyed.
    internal static bool IsFieldDrop(Item item) {
        return item.Transfer != null && !item.IsExpired() && item.Transfer.Flag.HasFlag(TransferFlag.Trade)
               && item.Transfer.Binding == null;
    }
}
