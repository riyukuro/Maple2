using Maple2.Model.Error;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Game.Packets;

// DungeonMatch 0xEF (CSceneStateGame::OnDungeonMatch, scenestategame.cpp:5184): a mode byte; the client reads only
// mode 0, the error.
public static class DungeonMatchPacket {
    // Mode 0, OnDungeonMatchError (uidungeonhelper.cpp:333): the error, an int (the {0} of overMaxRegisterUser) and a
    // string (the {0} of hasDungeonMatchCooldownParty).
    public static ByteWriter Error(DungeonMatchError error, int param = 0, string name = "") {
        var pWriter = Packet.Of(SendOp.DungeonMatch);
        pWriter.WriteByte(0);
        pWriter.WriteByte((byte) error);
        pWriter.WriteInt(param);
        pWriter.WriteUnicodeString(name);

        return pWriter;
    }
}
