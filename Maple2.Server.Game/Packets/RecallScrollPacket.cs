using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Model;

namespace Maple2.Server.Game.Packets;

public static class RecallScrollPacket {
    private enum Command : byte {
        Close = 0,
        Notify = 1,
    }

    // CSceneStateGame::OnRecallScroll: u8 mode, u8 type; mode 1 adds the caller's name, an unused int,
    // the map id, an unused int and the end time (server time, seconds) for the notify dialog's countdown.
    public static ByteWriter Notify(RecallScrollType type, string callerName, int mapId, long endTime) {
        var pWriter = Packet.Of(SendOp.RecallScroll);
        pWriter.WriteByte((byte) Command.Notify);
        pWriter.WriteByte((byte) type);
        pWriter.WriteUnicodeString(callerName);
        pWriter.WriteInt();
        pWriter.WriteInt(mapId);
        pWriter.WriteInt();
        pWriter.WriteLong(endTime);

        return pWriter;
    }

    // Mode 0 closes that type's notice in the notify dialog.
    public static ByteWriter Close(RecallScrollType type) {
        var pWriter = Packet.Of(SendOp.RecallScroll);
        pWriter.WriteByte((byte) Command.Close);
        pWriter.WriteByte((byte) type);

        return pWriter;
    }
}
