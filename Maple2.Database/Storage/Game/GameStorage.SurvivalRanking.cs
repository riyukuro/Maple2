using Maple2.Database.Model.Ranking;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Database.Storage;

public partial class GameStorage {
    public partial class Request {
        // The records of a Maple Survival season (survival-record; a squad season is stored under
        // SurvivalRules.SquadSeasonOffset plus its id), read with their characters (a row whose character is gone is
        // not shown).
        private IQueryable<SurvivalRow> SurvivalRows(int recordSeason) {
            return from record in Context.SurvivalRecord.AsNoTracking()
                   join character in Context.Character.AsNoTracking() on record.CharacterId equals character.Id
                   where record.SeasonId == recordSeason
                   select new SurvivalRow {
                       CharacterId = record.CharacterId,
                       Rating = record.Rating,
                       Games = record.Games,
                       Wins = record.Wins,
                       Kills = record.Kills,
                       LastGame = record.LastGame,
                       Name = character.Name,
                       Profile = character.Profile,
                   };
        }

        // Chosen (no client or table order): the highest rating first, then more wins, then the character id.
        private static IQueryable<SurvivalRow> InSurvivalOrder(IQueryable<SurvivalRow> rows) {
            return rows.OrderByDescending(row => row.Rating)
                .ThenByDescending(row => row.Wins)
                .ThenBy(row => row.CharacterId);
        }

        private static SurvivalRankInfo ToRankInfo(SurvivalRow row, int rank) {
            return new SurvivalRankInfo(rank, row.CharacterId, row.Name, row.Profile.Picture ?? string.Empty, row.Rating, row.Games,
                row.Wins, row.Kills, row.LastGame);
        }

        // The first 200 of the board.
        public IList<SurvivalRankInfo> GetSurvivalRankings(int recordSeason) {
            return InSurvivalOrder(SurvivalRows(recordSeason))
                .Take(200)
                .AsEnumerable()
                .Select((row, index) => ToRankInfo(row, index + 1))
                .ToList();
        }

        // The rank of a character: 1 + the rows that come before its row; null when it has no record that season.
        public SurvivalRankInfo? GetSurvivalRankInfo(int recordSeason, long characterId) {
            SurvivalRow? row = SurvivalRows(recordSeason).FirstOrDefault(row => row.CharacterId == characterId);
            if (row == null) {
                return null;
            }
            int before = SurvivalRows(recordSeason).Count(other => other.Rating > row.Rating
                || (other.Rating == row.Rating && (other.Wins > row.Wins
                    || (other.Wins == row.Wins && other.CharacterId < row.CharacterId))));
            return ToRankInfo(row, before + 1);
        }

        // The search by name finds the character as the trophy search does (GetCharacterId, any case).
        public SurvivalRankInfo? GetSurvivalRankInfo(int recordSeason, string name) {
            long characterId = GetCharacterId(name);
            return characterId == default ? null : GetSurvivalRankInfo(recordSeason, characterId);
        }

        private class SurvivalRow {
            public long CharacterId { get; init; }
            public int Rating { get; init; }
            public int Games { get; init; }
            public int Wins { get; init; }
            public int Kills { get; init; }
            public long LastGame { get; init; }
            public string Name { get; init; } = string.Empty;
            public Model.Profile Profile { get; init; } = null!;
        }
    }
}
