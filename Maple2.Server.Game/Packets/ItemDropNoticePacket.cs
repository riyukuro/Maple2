using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Game.Packets;

public static class ItemDropNoticePacket {
    // Client 2525 CSceneStateGame::OnItemDropNotice (scenestategame.cpp:8022): text player name, then the three
    // values of the item link helper FUN_140d2e2b0 (unit_140d2a7e0_chatlinkhelpers.cpp:716): int item id,
    // int grade, long uid. The id names the item when the reader does not hold the uid.
    public static ByteWriter Notice(string playerName, Item item) {
        var pWriter = Packet.Of(SendOp.ItemDropNotice);
        pWriter.WriteUnicodeString(playerName);
        pWriter.WriteInt(item.Id);
        pWriter.WriteInt(item.Rarity);
        pWriter.WriteLong(item.Uid);

        return pWriter;
    }
}
