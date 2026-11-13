using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Web.Packet;

namespace Maple2.Server.Web.Controllers;

// The answers to the guild championship board of irrq.aspx (the ranking dialog's board 5). The tab of the current
// season asks for 0x35 (0x33 for the own guild's rank) without a season, the tabs of the seasons before ask for 0x36
// (0x34) with the season id. The board is the guilds' season scores the World keeps (GameStorage.GetGuildPvpStandings).
public static class GuildPvpRanking {
    // The season of a request: the current one (seasondata_guildpvp at the time) for 0x35 and 0x33, else the season
    // id it asks for.
    public static int Season(GameRankingType type, int seasonId, IReadOnlyDictionary<int, SeasonDataTable.Entry> seasons, DateTime now) {
        return type is GameRankingType.GuildPvp or GameRankingType.GuildPvpPersonal
            ? GuildPvp.CurrentSeason(seasons, now)?.Id ?? 0
            : seasonId;
    }

    // The board's rows in order, ranked from 1, each with its grade in the season (CGuildInfo::GetGrade).
    public static IList<GuildPvpRankRow> Rows(IList<GuildPvpStanding> standings, SeasonDataTable.Entry? season) {
        IReadOnlyList<int> grades = season?.Grades ?? [];
        return standings.Select((standing, index) => new GuildPvpRankRow(index + 1, standing, GuildPvp.Grade(standing.Score, grades)))
            .ToList();
    }

    // 0x35, 0x36: the rows; with a search name, the row of that guild (none when it did not fight that season).
    public static ByteWriter Board(GameStorage.Request db, GameRankingType type, int seasonId,
                                   IReadOnlyDictionary<int, SeasonDataTable.Entry> seasons, string userName) {
        if (seasonId == 0) {
            return InGameRankPacket.GuildPvp(type, []);
        }
        seasons.TryGetValue(seasonId, out SeasonDataTable.Entry? season);
        IList<GuildPvpRankRow> rows = Rows(db.GetGuildPvpStandings(seasonId), season);
        if (!string.IsNullOrEmpty(userName)) {
            rows = rows.Where(row => row.Standing.Name == userName).ToList();
        }
        return InGameRankPacket.GuildPvp(type, rows);
    }

    // 0x33, 0x34: the rank of the character's guild on the board, 0 without a guild or a match that season.
    public static ByteWriter Personal(GameStorage.Request db, GameRankingType type, int seasonId, long characterId) {
        long guildId = seasonId == 0 ? 0 : db.GetGuildIdByCharacterId(characterId);
        if (guildId == 0) {
            return InGameRankPacket.PersonalRank(type, 0);
        }
        int index = db.GetGuildPvpStandings(seasonId).ToList().FindIndex(standing => standing.GuildId == guildId);
        return InGameRankPacket.PersonalRank(type, index + 1);
    }
}

public record GuildPvpRankRow(int Rank, GuildPvpStanding Standing, int Grade);
