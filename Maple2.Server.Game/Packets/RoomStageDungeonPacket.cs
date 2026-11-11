using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Game.Packets;

public static class RoomStageDungeonPacket {
    private enum Command : byte {
        Set = 0,
        Update = 1,
    }

    // OnRoomStageDungeon case 0 (networkservice.cpp:1085) reads an int, a byte and a bool, then calls CreateStage.
    public static ByteWriter Set(int dungeonId, byte flag = 0, bool enabled = false) {
        var pWriter = Packet.Of(SendOp.RoomStageDungeon);
        pWriter.WriteByte((byte) Command.Set);
        pWriter.WriteInt(dungeonId);
        pWriter.WriteByte(flag);
        pWriter.WriteBool(enabled);

        return pWriter;
    }

    // OnRoomStageDungeon case 1 (networkservice.cpp:1092) reads nothing more: it calls ClearStage.
    public static ByteWriter Update() {
        var pWriter = Packet.Of(SendOp.RoomStageDungeon);
        pWriter.WriteByte((byte) Command.Update);

        return pWriter;
    }
}
