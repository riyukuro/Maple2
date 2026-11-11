using System;
using System.Collections.Generic;
using Maple2.Database.Model.Ranking;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game.Dungeon;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Web.Packet;

namespace Maple2.Server.Web.Controllers;

// The answers to the Colosseum board of irrq.aspx (the ranking dialog's board 0x10). The request's ranking id is the
// job tab (0 for every job, else a job code) and its season id the week asked for: the tab of the current season
// asks for 0xcb (0xc9 for the own rank) without a season, the tabs of the seasons before ask for 0xcc (0xca) with it.
// The board is the characters' best run of the season (colosseum-record).
public static class ColosseumRanking {
    // The season of a request: the current one (seasondata_bosscolosseum at the time) for 0xcb and 0xc9, else the
    // season id it asks for.
    public static int Season(GameRankingType type, int seasonId, IReadOnlyDictionary<int, SeasonDataTable.Entry> seasons, DateTime now) {
        return type is GameRankingType.Colosseum or GameRankingType.ColosseumPersonal
            ? ColosseumBest.SeasonAt(seasons, now)
            : seasonId;
    }

    // 0xcb, 0xcc: the rows; with a search name, the row of that character (none when it has no run that season).
    public static ByteWriter Board(GameStorage.Request db, GameRankingType type, int seasonId, int jobCode, string userName) {
        if (seasonId == 0) {
            return InGameRankPacket.Colosseum(type, []);
        }
        if (string.IsNullOrEmpty(userName)) {
            return InGameRankPacket.Colosseum(type, db.GetColosseumRankings(seasonId, jobCode));
        }

        ColosseumRankInfo? info = db.GetColosseumRankInfo(seasonId, jobCode, userName);
        return InGameRankPacket.Colosseum(type, info != null ? new List<ColosseumRankInfo> { info } : []);
    }

    // 0xc9, 0xca: the character's rank on the board, 0 when it has no run that season.
    public static ByteWriter Personal(GameStorage.Request db, GameRankingType type, int seasonId, int jobCode, long characterId) {
        if (seasonId == 0) {
            return InGameRankPacket.PersonalRank(type, 0);
        }
        return InGameRankPacket.PersonalRank(type, db.GetColosseumRankInfo(seasonId, jobCode, characterId)?.Rank ?? 0);
    }
}
