using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Game.Packets;

public static class WorldChampionPacket {
    public const byte NoSeasonMode = 11;

    // Mode 11 has no body: the client shows s_world_champion_msg_not_found_season
    // (uiworldchampionrewarddialog.cpp:248).
    public static ByteWriter NoSeason() {
        var pWriter = Packet.Of(SendOp.WorldChampion);
        pWriter.WriteByte(NoSeasonMode);

        return pWriter;
    }
}
