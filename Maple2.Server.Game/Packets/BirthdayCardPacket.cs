using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Game.Packets;

// BirthdayCard 0x111 (CSceneStateGame::OnBirthdayCard): mode 1 sets a player's birthday (PC+0x11e8, read by
// UIBirthdayCardDialog). The client reads no other mode.
public static class BirthdayCardPacket {
    private enum Command : byte {
        Set = 1,
    }

    public static ByteWriter Set(int objectId, long birthday) {
        var pWriter = Packet.Of(SendOp.BirthdayCard);
        pWriter.WriteByte((byte) Command.Set);
        pWriter.WriteInt(objectId);
        pWriter.WriteLong(birthday);

        return pWriter;
    }
}
