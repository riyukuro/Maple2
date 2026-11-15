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

// The answers to the Maple Survival board of irrq.aspx (the restored client's ranking dialog, board 0xd; live KMS has
// no such board). Sub 1 (and the lobby grade button's sub 0) is solo, sub 4 squad; the type picks the board: the
// current season asks for 0xad / 0xc1 (0xab / 0xbf for the own rank), the ones before for 0xae / 0xc2 (0xac / 0xc0),
// each with the season id as its date (GMS CGameRankingCli::BuildKey 0x727480, BuildMyRankKey 0x7277c0). The board is
// the characters' season ratings (survival-record): a solo season is stored under its id (the maplesurvivalopenperiod
// period, the same ids and spans as SeasonData_MapleSurvival), a squad season under SurvivalRules.SquadSeasonOffset
// plus its SeasonData_MapleSurvivalSquad id.
public static class SurvivalRanking {
    public static bool IsSquad(GameRankingType type) {
        return type is GameRankingType.SurvivalSquad or GameRankingType.SurvivalSquadPreviousSeason
            or GameRankingType.SurvivalSquadPersonal or GameRankingType.SurvivalSquadPersonalPreviousSeason;
    }

    private static bool IsCurrent(GameRankingType type) {
        return type is GameRankingType.Survival or GameRankingType.SurvivalPersonal
            or GameRankingType.SurvivalSquad or GameRankingType.SurvivalSquadPersonal;
    }

    // The season table of a request's board.
    public static IReadOnlyDictionary<int, SeasonDataTable.Entry> Seasons(GameRankingType type, SeasonDataTable table) {
        return IsSquad(type) ? table.SurvivalSquad : table.Survival;
    }

    // The season of a request: the one running now (the one the game keeps the records in) for the current boards,
    // else the season id it asks for. 0: none.
    public static int Season(GameRankingType type, int seasonId, IReadOnlyDictionary<int, SeasonDataTable.Entry> seasons, DateTime now) {
        return IsCurrent(type) ? SurvivalRules.SeasonAt(seasons, now) : seasonId;
    }

    // The survival-record season id the board's records are kept under.
    public static int RecordSeason(GameRankingType type, int season) {
        return IsSquad(type) ? SurvivalRules.SquadSeasonOffset + season : season;
    }

    // 0xad, 0xae, 0xc1, 0xc2: the rows; with a search name, the row of that character (none when it has no record that
    // season). The grades are the season's thresholds (none for a season the table does not have).
    public static ByteWriter Board(GameStorage.Request db, GameRankingType type, int season, IReadOnlyDictionary<int, SeasonDataTable.Entry> seasons, string userName) {
        IReadOnlyList<int> grades = seasons.TryGetValue(season, out SeasonDataTable.Entry? entry) ? entry.Grades : [];
        if (season <= 0) {
            return InGameRankPacket.Survival(type, [], grades);
        }
        int recordSeason = RecordSeason(type, season);
        if (string.IsNullOrEmpty(userName)) {
            return InGameRankPacket.Survival(type, db.GetSurvivalRankings(recordSeason), grades);
        }

        SurvivalRankInfo? info = db.GetSurvivalRankInfo(recordSeason, userName);
        return InGameRankPacket.Survival(type, info != null ? new List<SurvivalRankInfo> { info } : [], grades);
    }

    // 0xab, 0xac, 0xbf, 0xc0: the character's rank on the board, 0 when it has no record that season.
    public static ByteWriter Personal(GameStorage.Request db, GameRankingType type, int season, long characterId) {
        if (season <= 0) {
            return InGameRankPacket.SurvivalPersonalRank(type, 0);
        }
        return InGameRankPacket.SurvivalPersonalRank(type, db.GetSurvivalRankInfo(RecordSeason(type, season), characterId)?.Rank ?? 0);
    }
}
