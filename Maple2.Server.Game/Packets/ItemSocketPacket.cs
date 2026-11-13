using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Tools.Extensions;
using static Maple2.Model.Error.ItemSocketError;

namespace Maple2.Server.Game.Packets;

public static class ItemSocketPacket {
    public enum Command : byte {
        UnlockSocket = 1,
        StageUnlockSocket = 3,
        UpgradeGemstone = 5,
        StageUpgradeGemstone = 7,
        EquipGemstone = 9,
        UnequipGemstone = 11,
        TransferSocket = 13,
        Unknown = 16,
        Error = 18,
    }

    public static ByteWriter UnlockSocket(bool success, Item item) {
        if (item.Socket == null) {
            return Error(error: s_itemsocketsystem_error_socket_unlock_all);
        }

        var pWriter = Packet.Of(SendOp.ItemSocketSystem);
        pWriter.Write<Command>(Command.UnlockSocket);
        pWriter.WriteBool(success);
        pWriter.WriteLong(item.Uid);
        if (success) {
            // The row of the socket just unlocked, from 0 (FUN_140629140 m_nByte, "unlockSocketIndex"): the unlock
            // page's setSocket plays the unlock effect on the row with this index, and the open sockets come first.
            // Called after UnlockSlots++, so the new socket is the last open one. MaxSlots matched no row.
            pWriter.WriteByte((byte) (item.Socket.UnlockSlots - 1));
            pWriter.WriteClass<ItemSocket>(item.Socket);
            // Used for mutating item trade info?
            pWriter.WriteBool(item.Transfer != null);
            if (item.Transfer != null) {
                pWriter.WriteClass<ItemTransfer>(item.Transfer);
            }
        }

        return pWriter;
    }

    public static ByteWriter StageUnlockSocket(Item item, float rate) {
        var pWriter = Packet.Of(SendOp.ItemSocketSystem);
        pWriter.Write<Command>(Command.StageUnlockSocket);
        pWriter.WriteLong();
        pWriter.Write<sbyte>(-1);
        pWriter.WriteLong(item.Uid);
        pWriter.WriteFloat(rate);

        return pWriter;
    }

    public static ByteWriter UpgradeGemstone(long uid, bool success, Item gem) {
        var pWriter = Packet.Of(SendOp.ItemSocketSystem);
        pWriter.Write<Command>(Command.UpgradeGemstone);
        pWriter.WriteLong(uid);
        pWriter.WriteByte();
        pWriter.WriteBool(success);
        pWriter.WriteByte(Constant.GemstoneGrade);
        pWriter.WriteLong(gem.Uid);
        pWriter.WriteBool(true);
        if (true) {
            pWriter.WriteGemstone(gem);
        }

        return pWriter;
    }

    // A gem in an equipment socket. The client's reader (FUN_140629a30, itemsocketsystemutil.cpp:1005) takes the byte
    // after the uid as the socket slot, and the upgrade page stores the new gem in that slot of the item
    // (FUN_140f787a0, SetGemInfo), so it must be the slot the gem is in.
    public static ByteWriter UpgradeGemstone(long uid, byte slot, bool success, long clientUid, ItemGemstone gem) {
        var pWriter = Packet.Of(SendOp.ItemSocketSystem);
        pWriter.Write<Command>(Command.UpgradeGemstone);
        pWriter.WriteLong(uid);
        pWriter.WriteByte(slot);
        pWriter.WriteBool(success);
        pWriter.WriteByte(Constant.GemstoneGrade);
        pWriter.WriteLong(clientUid);
        pWriter.WriteBool(true);
        if (true) {
            pWriter.WriteGemstone(gem);
        }

        return pWriter;
    }

    public static ByteWriter StageUpgradeGemstone(long uid, sbyte slot, long gemUid, float rate) {
        var pWriter = Packet.Of(SendOp.ItemSocketSystem);
        pWriter.Write<Command>(Command.StageUpgradeGemstone);
        pWriter.WriteLong(uid);
        pWriter.Write<sbyte>(slot);
        pWriter.WriteLong(gemUid);
        pWriter.WriteFloat(rate);

        return pWriter;
    }

    public static ByteWriter EquipGemstone(long itemUid, byte slot, ItemGemstone? gem) {
        var pWriter = Packet.Of(SendOp.ItemSocketSystem);
        pWriter.Write<Command>(Command.EquipGemstone);
        pWriter.WriteLong(itemUid);
        pWriter.WriteByte(slot);

        pWriter.WriteBool(gem != null);
        if (gem != null) {
            pWriter.WriteGemstone(gem);
        }

        return pWriter;
    }

    public static ByteWriter UnequipGemstone(long itemUid, byte slot, long gemUid) {
        var pWriter = Packet.Of(SendOp.ItemSocketSystem);
        pWriter.Write<Command>(Command.UnequipGemstone);
        // Client 2525 FUN_140628be0 (itemsocketsystemutil.cpp:703): long, byte, long.
        pWriter.WriteLong(itemUid);
        pWriter.WriteByte(slot);
        pWriter.WriteLong(gemUid);

        return pWriter;
    }

    // Client 2525 FUN_1406295b0 (itemsocketsystemutil.cpp:933), called by the socket transfer page for 13:
    // long source uid, source socket block, long target uid, target socket block, bool, then the target's
    // transfer block when the bool is set. The client stops reading when a uid is not the item it staged.
    public static ByteWriter TransferSocket(Item source, Item target) {
        var pWriter = Packet.Of(SendOp.ItemSocketSystem);
        pWriter.Write<Command>(Command.TransferSocket);
        pWriter.WriteLong(source.Uid);
        pWriter.WriteClass<ItemSocket>(source.Socket ?? ItemSocket.Default);
        pWriter.WriteLong(target.Uid);
        pWriter.WriteClass<ItemSocket>(target.Socket ?? ItemSocket.Default);
        pWriter.WriteBool(target.Transfer != null);
        if (target.Transfer != null) {
            pWriter.WriteClass<ItemTransfer>(target.Transfer);
        }

        return pWriter;
    }

    public static ByteWriter Unknown() {
        var pWriter = Packet.Of(SendOp.ItemSocketSystem);
        pWriter.Write<Command>(Command.Unknown);

        return pWriter;
    }

    public static ByteWriter Error(byte code = 0, ItemSocketError error = s_itemsocketsystem_error_server_default) {
        var pWriter = Packet.Of(SendOp.ItemSocketSystem);
        pWriter.Write<Command>(Command.Error);
        pWriter.WriteByte(code);
        pWriter.Write<ItemSocketError>(error);

        return pWriter;
    }

    // Gemstone from inventory
    private static void WriteGemstone(this IByteWriter writer, Item gem) {
        writer.WriteInt(gem.Id);
        writer.WriteBool(gem.Transfer?.Binding != null);
        if (gem.Transfer?.Binding != null) {
            writer.WriteClass<ItemBinding>(gem.Transfer.Binding);
        }

        writer.WriteBool(gem.IsLocked);
        if (gem.IsLocked) {
            writer.WriteByte();
            writer.WriteLong(gem.UnlockTime);
        }
    }

    // Gemstone in socket
    private static void WriteGemstone(this IByteWriter writer, ItemGemstone gem) {
        writer.WriteInt(gem.ItemId);
        writer.WriteBool(gem.Binding != null);
        if (gem.Binding != null) {
            writer.WriteClass<ItemBinding>(gem.Binding);
        }

        writer.WriteBool(gem.IsLocked);
        if (gem.IsLocked) {
            writer.WriteByte();
            writer.WriteLong(gem.UnlockTime);
        }
    }
}
