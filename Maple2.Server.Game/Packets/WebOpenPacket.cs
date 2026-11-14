using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Game.Packets;

// WebOpen 0x9b (CSceneStateGame::OnWebOpen, scenestategame.cpp:3743): u8 kind, then the url (a unicode string). The
// client expands the url's prefix ("maview:" to the ServerEnter maview url) and opens it: kind 1 in the ranking dialog
// on the tab whose gameRankingCategory url the url holds when InGameRank is on, else in the maview browser when Maview
// is on; kind 0 in the system browser; kind 2 in the maview browser when AdventureBar is on.
public static class WebOpenPacket {
    public static ByteWriter Open(byte kind, string url) {
        var pWriter = Packet.Of(SendOp.WebOpen);
        pWriter.WriteByte(kind);
        pWriter.WriteUnicodeString(url);
        return pWriter;
    }
}
