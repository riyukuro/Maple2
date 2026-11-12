using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Game.Packets;

// HideAndSeek 0x12B (client CSceneStateGame::OnHideAndSeek): a byte mode. Mode 3 is an int object id; for the
// client's own character it ends the hide-and-seek slot mode, shows the hud again (the hide reason the arcade's
// PlayArcade 0 set) and closes the arcade life bar dialog. Only mode 3 is built: the arcades close their life bar
// with it.
public static class HideAndSeekPacket {
    private enum Command : byte {
        End = 3,
    }

    public static ByteWriter End(int objectId) {
        var pWriter = Packet.Of(SendOp.HideAndSeek);
        pWriter.WriteByte((byte) Command.End);
        pWriter.WriteInt(objectId);
        return pWriter;
    }
}
