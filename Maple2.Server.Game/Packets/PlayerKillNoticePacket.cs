using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Game.Packets;

// Client: CSceneStateGame::OnPlayerKillNotice (client/ms2/scenestategame.cpp:4344) reads a mode byte first.
public static class PlayerKillNoticePacket {
    private enum Command : byte {
        Kill = 0,
        Notice = 1,
    }

    // Mode 0: the killer's and the victim's names, for the kill log dialog.
    public static ByteWriter Kill(string killerName, string victimName) {
        var pWriter = Packet.Of(SendOp.PlayerKillNotice);
        pWriter.WriteByte((byte) Command.Kill);
        pWriter.WriteUnicodeString(killerName);
        pWriter.WriteUnicodeString(victimName);

        return pWriter;
    }

    // Mode 1: an interface text shown as a system notice.
    public static ByteWriter Notice(InterfaceText text) {
        var pWriter = Packet.Of(SendOp.PlayerKillNotice);
        pWriter.WriteByte((byte) Command.Notice);
        pWriter.WriteClass<InterfaceText>(text);

        return pWriter;
    }
}
