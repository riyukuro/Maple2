using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Game.Packets;

public static class FieldWarPacket {
    private enum Command : byte {
        Announce = 0,
    }

    // CSceneStateGame::OnFieldWar mode 0: the war id (a row of the client's table/fieldwardata.xml, else the client
    // shows nothing) and the uid it keeps with the popup. Its side popup's button sends SidePopup 1.
    public static ByteWriter Announce(int fieldWarId, long uid) {
        var pWriter = Packet.Of(SendOp.FieldWar);
        pWriter.Write<Command>(Command.Announce);
        pWriter.WriteInt(fieldWarId);
        pWriter.WriteLong(uid);
        return pWriter;
    }
}
