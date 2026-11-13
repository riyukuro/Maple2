using System;
using System.Collections.Generic;
using Maple2.Database.Model.Ranking;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game.Pvp;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Web.Packet;

namespace Maple2.Server.Web.Controllers;

// The answers to the rank duel board of irrq.aspx (the ranking dialog's board 4). Its tabs are the PvP seasons
// (UIGameRankingDialog::GetCategoryTabs): the current one asks for 0x2b (0x29 for the own rank) without a season, the
// ones before ask for 0x2c (0x2a) with the season id (CGameRankingCli::BuildKey, gamerankingcli.cpp:502-508 and
// 616-622). The board is the characters' rank duel ratings of the season (rank-duel-record, rank-duel-season).
public static class RankDuelRanking {
    // The season of a request: the current PvP season (the one the game keeps the records in) for 0x2b and 0x29,
    // else the season id it asks for.
    public static int Season(GameRankingType type, int seasonId, IReadOnlyDictionary<int, SeasonDataTable.Entry> seasons, DateTime now) {
        return type is GameRankingType.Pvp or GameRankingType.PvpPersonal
            ? RankDuelRules.SeasonAt(seasons, now)
            : seasonId;
    }

    // 0x2b, 0x2c: the rows; with a search name, the row of that character (none when it has no record that season).
    public static ByteWriter Board(GameStorage.Request db, GameRankingType type, int season, string userName) {
        if (season == 0) {
            return InGameRankPacket.Pvp(type, []);
        }
        if (string.IsNullOrEmpty(userName)) {
            return InGameRankPacket.Pvp(type, db.GetRankDuelRankings(season));
        }

        RankDuelRankInfo? info = db.GetRankDuelRankInfo(season, userName);
        return InGameRankPacket.Pvp(type, info != null ? new List<RankDuelRankInfo> { info } : []);
    }

    // 0x29, 0x2a: the character's rank on the board, 0 when it has no record that season.
    public static ByteWriter Personal(GameStorage.Request db, GameRankingType type, int season, long characterId) {
        if (season == 0) {
            return InGameRankPacket.PersonalRank(type, 0);
        }
        return InGameRankPacket.PersonalRank(type, db.GetRankDuelRankInfo(season, characterId)?.Rank ?? 0);
    }
}
