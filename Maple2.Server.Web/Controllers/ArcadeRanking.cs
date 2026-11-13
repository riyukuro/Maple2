using System.Collections.Generic;
using Maple2.Database.Model.Ranking;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.PacketLib.Tools;
using Maple2.Server.Web.Packet;

namespace Maple2.Server.Web.Controllers;

// The answers to the arcade boards of irrq.aspx (the ranking dialog's board 10, and the five boards the arcade main
// dialog asks for when it opens, uiarcademaindialog.cpp:726-741). Every request carries both keys
// (gamerankingcli.cpp:535-545 and 645-655): the ranking id is the arcade game (1-5) and the season id the arcade
// season, the current one for 0x67 (0x65 for the own rank) and the one asked for with 0x68 (0x66). The board is the
// characters' best score of the game in the season (arcade-record).
public static class ArcadeRanking {
    // 0x67, 0x68: the rows; with a search name, the row of that character (none when it has no score that season).
    public static ByteWriter Board(GameStorage.Request db, GameRankingType type, int arcadeId, int seasonId, string userName) {
        if (seasonId == 0 || arcadeId == 0) {
            return InGameRankPacket.Arcade(type, []);
        }
        if (string.IsNullOrEmpty(userName)) {
            return InGameRankPacket.Arcade(type, db.GetArcadeRankings(arcadeId, seasonId));
        }

        ArcadeRankInfo? info = db.GetArcadeRankInfo(arcadeId, seasonId, userName);
        return InGameRankPacket.Arcade(type, info != null ? new List<ArcadeRankInfo> { info } : []);
    }

    // 0x65, 0x66: the character's rank on the board, 0 when it has no score that season.
    public static ByteWriter Personal(GameStorage.Request db, GameRankingType type, int arcadeId, int seasonId, long characterId) {
        if (seasonId == 0 || arcadeId == 0) {
            return InGameRankPacket.PersonalRank(type, 0);
        }
        return InGameRankPacket.PersonalRank(type, db.GetArcadeRankInfo(arcadeId, seasonId, characterId)?.Rank ?? 0);
    }
}
