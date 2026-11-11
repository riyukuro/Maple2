using System.Collections.Generic;
using Maple2.Database.Model.Ranking;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.PacketLib.Tools;
using Maple2.Server.Web.Packet;

namespace Maple2.Server.Web.Controllers;

// The answers to the boss boards of irrq.aspx. The ranking dialog's boss boards (7, 8, 9, 0xf and 0x11) have one
// tab per dungeon room with a BossRanking, and the request's ranking id is that BossRanking. Each tab has three
// modes: the clears per character (0x48, the own rank 0x47), the first clears (0x51) and the fastest clears (0x5b).
// The rows come from the clears the game kept (boss-clear); no season is kept.
public static class BossRanking {
    // 0x48 (with a search name, the row of that character, none when it has no clear), 0x51 and 0x5b.
    public static ByteWriter Board(GameStorage.Request db, GameRankingType type, int rankingId, string userName) {
        switch (type) {
            case GameRankingType.BossFirstClears:
                return InGameRankPacket.BossClears(type, db.GetBossFirstClears(rankingId));
            case GameRankingType.BossShortestClears:
                return InGameRankPacket.BossClears(type, db.GetBossShortestClears(rankingId));
        }
        if (string.IsNullOrEmpty(userName)) {
            return InGameRankPacket.BossClearCount(rankingId, db.GetBossClearCounts(rankingId));
        }

        BossClearCountRankInfo? info = db.GetBossClearCount(rankingId, userName);
        return InGameRankPacket.BossClearCount(rankingId, info != null ? new List<BossClearCountRankInfo> { info } : []);
    }

    // 0x47: the character's rank on the clear count board, 0 without a clear.
    public static ByteWriter Personal(GameStorage.Request db, int rankingId, long characterId) {
        return InGameRankPacket.PersonalRank(GameRankingType.BossClearCountPersonal, db.GetBossClearCount(rankingId, characterId)?.Rank ?? 0);
    }
}
