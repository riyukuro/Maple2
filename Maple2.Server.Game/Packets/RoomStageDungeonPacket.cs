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
        pWriter.Write<Command>(Command.Set);
        pWriter.WriteInt(dungeonId);
        pWriter.WriteByte(flag);
        pWriter.WriteBool(enabled);

        return pWriter;
    }

    public static ByteWriter Update(int value) {
        var pWriter = Packet.Of(SendOp.RoomStageDungeon);
        pWriter.Write<Command>(Command.Update);
        pWriter.WriteInt(value);

        return pWriter;
    }
}
