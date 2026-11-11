using System;
using System.Collections.Generic;
using Maple2.Database.Model.Ranking;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Web.Packet;

namespace Maple2.Server.Web.Controllers;

// The answers to the house commendation board of irrq.aspx (the ranking dialog's board 6). Its tab of the current
// month asks for 0x3f (0x3d for the own rank) without a date; the tabs of the months before ask for 0x40 (0x3e) with
// the yyyymm month in the season id (FUN_140b3df90). The board is the homes' commendations of that month
// (home-commend), the own rank is the asking account's home.
public static class HomeCommendRanking {
    // The month a request asks for: [begin, end) in epoch seconds, null for a date that is no month.
    public static (long Begin, long End)? Month(GameRankingType type, int seasonId, long now, TimeZoneInfo zone) {
        return type is GameRankingType.Ugc or GameRankingType.UgcPersonal
            ? HomeCommendRules.MonthSpan(now, zone)
            : HomeCommendRules.MonthBounds(seasonId, zone);
    }

    // 0x3f, 0x40: the rows; with a search name, the row of that character's home (none when it has no commendation
    // that month).
    public static ByteWriter Board(GameStorage.Request db, GameRankingType type, (long Begin, long End)? month, string userName) {
        if (month == null) {
            return InGameRankPacket.Ugc(type, []);
        }
        (long begin, long end) = month.Value;
        if (string.IsNullOrEmpty(userName)) {
            return InGameRankPacket.Ugc(type, db.GetHomeCommendRankings(begin, end));
        }

        HomeCommendRankInfo? info = db.GetHomeCommendRankInfo(begin, end, userName);
        return InGameRankPacket.Ugc(type, info != null ? new List<HomeCommendRankInfo> { info } : []);
    }

    // 0x3d, 0x3e: the rank of the account's home, 0 when it has no commendation that month.
    public static ByteWriter Personal(GameStorage.Request db, GameRankingType type, (long Begin, long End)? month, long accountId) {
        if (month == null) {
            return InGameRankPacket.PersonalRank(type, 0);
        }
        (long begin, long end) = month.Value;
        return InGameRankPacket.PersonalRank(type, db.GetHomeCommendRankInfo(begin, end, accountId)?.Rank ?? 0);
    }
}
