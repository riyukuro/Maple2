using Maple2.Model.Enum;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Game.Packets;

// The client reads int actor object id, int type, int value and shows the value as a recovery number over that
// player (the "Recover" damage-text grade, which also feeds the DPS meter): type 4 (Health), 7 (Spirit) and
// 10 (Stamina). Any other type shows nothing.
public static class UiTextPacket {
    public static ByteWriter Recover(int objectId, BasicAttribute attribute, int amount) {
        var pWriter = Packet.Of(SendOp.UiText);
        pWriter.WriteInt(objectId);
        pWriter.WriteInt((int) attribute);
        pWriter.WriteInt(amount);
        return pWriter;
    }
}
