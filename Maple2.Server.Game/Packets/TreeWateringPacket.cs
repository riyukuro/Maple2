using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Game.Packets;

// SceneStateGame::OnTreeWatering (scenestategame.cpp): a mode, a result, and on a reward the day's count and limit.
public static class TreeWateringPacket {
    private enum Command : byte {
        Water = 2,
        Emotion = 3,
    }

    public static ByteWriter Water(byte result, int count = 0, int limit = 0) {
        return Of(Command.Water, result, count, limit);
    }

    public static ByteWriter Emotion(byte result, int count = 0, int limit = 0) {
        return Of(Command.Emotion, result, count, limit);
    }

    private static ByteWriter Of(Command command, byte result, int count, int limit) {
        var pWriter = Packet.Of(SendOp.TreeWatering);
        pWriter.WriteByte((byte) command);
        pWriter.WriteByte(result);
        if (result == TreeWateringEvent.Rewarded) {
            pWriter.WriteInt(count);
            pWriter.WriteInt(limit);
        }
        return pWriter;
    }
}
