using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Game.Packets;

public static class AdminPacket {

    // Mode 0: the account's GM level (AdminLevel). The client keeps 1 for a normal player.
    public static ByteWriter Level(byte level) {
        ByteWriter pWriter = Packet.Of(SendOp.Admin);
        pWriter.WriteByte();
        pWriter.WriteByte(level);

        return pWriter;
    }
}
