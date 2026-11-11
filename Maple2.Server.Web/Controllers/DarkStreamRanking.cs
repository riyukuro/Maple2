using System.Collections.Generic;
using Maple2.Database.Model.Ranking;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.PacketLib.Tools;
using Maple2.Server.Web.Packet;

namespace Maple2.Server.Web.Controllers;

// The answers to the Dark Stream boards of irrq.aspx. The request's ranking id is the board's job tab (0 for every
// job, else a job code: the client's ranking dialog adds a tab per job code under the board). The board is the
// characters' best scores (dark-stream-record); no season is kept, so the boards of the season before (0x22, 0x20)
// are empty.
public static class DarkStreamRanking {
    // 0x21 (and 0x22): the rows; with a search name, the row of that character (none when it has no score).
    public static ByteWriter Board(GameStorage.Request db, GameRankingType type, int jobCode, string userName) {
        if (type != GameRankingType.DarkDescent) {
            return InGameRankPacket.DarkStream(type, []);
        }
        if (string.IsNullOrEmpty(userName)) {
            return InGameRankPacket.DarkStream(type, db.GetDarkStreamRankings(jobCode));
        }

        DarkStreamRankInfo? info = db.GetDarkStreamRankInfo(jobCode, userName);
        return InGameRankPacket.DarkStream(type, info != null ? new List<DarkStreamRankInfo> { info } : []);
    }

    // 0x1f (and 0x20): the character's rank on the board, 0 when it has no score.
    public static ByteWriter Personal(GameStorage.Request db, GameRankingType type, int jobCode, long characterId) {
        if (type != GameRankingType.DarkDescentPersonal) {
            return InGameRankPacket.PersonalRank(type, 0);
        }
        return InGameRankPacket.PersonalRank(type, db.GetDarkStreamRankInfo(jobCode, characterId)?.Rank ?? 0);
    }
}
