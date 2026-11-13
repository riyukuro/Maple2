using Maple2.Model;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Game.Packets;

public static class ItemInventoryPacket {
    private enum Command : byte {
        Add = 0,
        Remove = 1,
        UpdateAmount = 2,
        Move = 3,
        Load = 6,
        NotifyNew = 7,
        LoadTab = 8,
        ExpandComplete = 10,
        Reset = 11,
        ExpandCount = 12,
        Error = 13,
        UpdateItem = 14,
    }

    public static ByteWriter Add(Item item) {
        var pWriter = Packet.Of(SendOp.ItemInventory);
        pWriter.Write<Command>(Command.Add);
        pWriter.WriteInt(item.Id);
        pWriter.WriteLong(item.Uid);
        pWriter.WriteShort(item.Slot);
        pWriter.WriteInt(item.Rarity);
        pWriter.WriteClass<Item>(item);

        return pWriter;
    }

    public static ByteWriter Remove(long uid) {
        var pWriter = Packet.Of(SendOp.ItemInventory);
        pWriter.Write<Command>(Command.Remove);
        pWriter.WriteLong(uid);

        return pWriter;
    }

    public static ByteWriter UpdateAmount(long uid, int amount) {
        var pWriter = Packet.Of(SendOp.ItemInventory);
        pWriter.Write<Command>(Command.UpdateAmount);
        pWriter.WriteLong(uid);
        pWriter.WriteInt(amount);

        return pWriter;
    }

    public static ByteWriter Move(long srcUid, short srcSlot, long dstUid, short dstSlot) {
        var pWriter = Packet.Of(SendOp.ItemInventory);
        pWriter.Write<Command>(Command.Move);
        pWriter.WriteLong(srcUid);
        pWriter.WriteShort(srcSlot);
        pWriter.WriteLong(dstUid);
        pWriter.WriteShort(dstSlot);

        return pWriter;
    }

    public static ByteWriter Load(ICollection<Item> items) {
        var pWriter = Packet.Of(SendOp.ItemInventory);
        pWriter.Write<Command>(Command.Load);
        pWriter.WriteShort((short) items.Count);
        foreach (Item item in items) {
            pWriter.WriteInt(item.Id);
            pWriter.WriteLong(item.Uid);
            pWriter.WriteShort(item.Slot);
            pWriter.WriteInt(item.Rarity);
            pWriter.WriteClass<Item>(item);
        }

        return pWriter;
    }

    public static ByteWriter NotifyNew(long uid, int amount) {
        var pWriter = Packet.Of(SendOp.ItemInventory);
        pWriter.Write<Command>(Command.NotifyNew);
        pWriter.WriteLong(uid);
        pWriter.WriteInt(amount);
        pWriter.WriteUnicodeString();

        return pWriter;
    }

    public static ByteWriter LoadTab(InventoryType type, ICollection<Item> items) {
        var pWriter = Packet.Of(SendOp.ItemInventory);
        pWriter.Write<Command>(Command.LoadTab);
        pWriter.WriteInt(type.ToClient());
        pWriter.WriteShort((short) items.Count);
        foreach (Item item in items) {
            pWriter.WriteInt(item.Id);
            pWriter.WriteLong(item.Uid);
            pWriter.WriteShort(item.Slot);
            pWriter.WriteInt(item.Rarity);
            pWriter.WriteClass<Item>(item);
        }

        return pWriter;
    }

    // s_msg_expand_inven_complete: Your inventory has been expanded.
    public static ByteWriter ExpandComplete() {
        var pWriter = Packet.Of(SendOp.ItemInventory);
        pWriter.Write<Command>(Command.ExpandComplete);

        return pWriter;
    }

    public static ByteWriter Reset(InventoryType type) {
        var pWriter = Packet.Of(SendOp.ItemInventory);
        pWriter.Write<Command>(Command.Reset);
        pWriter.WriteInt(type.ToClient());

        return pWriter;
    }

    public static ByteWriter ExpandCount(InventoryType type, int expansion) {
        var pWriter = Packet.Of(SendOp.ItemInventory);
        pWriter.Write<Command>(Command.ExpandCount);
        pWriter.WriteByte(type.ToClient());
        pWriter.WriteInt(expansion);

        return pWriter;
    }

    // The client's reader (scenestategame.cpp:3004-3016) reads an item id and a short after the two level errors, so
    // they go through ErrorRestrictedByLevel; sent here they would leave the client reading past the packet.
    public static ByteWriter Error(ItemInventoryError error) {
        if (error is ItemInventoryError.s_itemdrop_error_restricted_by_user_level_ex or ItemInventoryError.s_itempickup_error_restricted_by_user_level_ex) {
            throw new ArgumentException($"{error} carries an item and a level: use ErrorRestrictedByLevel", nameof(error));
        }
        var pWriter = Packet.Of(SendOp.ItemInventory);
        pWriter.Write<Command>(Command.Error);
        pWriter.WriteInt((int) error);

        return pWriter;
    }

    // Errors 40 and 41 (drop or pickup needs a level): int error, int item id, short level (the "{1}" of the message).
    public static ByteWriter ErrorRestrictedByLevel(bool pickup, int itemId, short level) {
        var pWriter = Packet.Of(SendOp.ItemInventory);
        pWriter.Write<Command>(Command.Error);
        pWriter.WriteInt((int) (pickup ? ItemInventoryError.s_itempickup_error_restricted_by_user_level_ex : ItemInventoryError.s_itemdrop_error_restricted_by_user_level_ex));
        pWriter.WriteInt(itemId);
        pWriter.WriteShort(level);

        return pWriter;
    }

    public static ByteWriter UpdateItem(Item item) {
        var pWriter = Packet.Of(SendOp.ItemInventory);
        pWriter.Write<Command>(Command.UpdateItem);
        pWriter.WriteLong(item.Uid);
        pWriter.WriteClass<Item>(item);

        return pWriter;
    }
}
