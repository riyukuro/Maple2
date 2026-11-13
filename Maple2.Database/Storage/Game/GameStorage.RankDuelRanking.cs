using Maple2.Database.Model.Ranking;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Database.Storage;

public partial class GameStorage {
    public partial class Request {
        // The standings of a PvP season, read with their characters (a row whose character is gone is not shown): the
        // records still in that season (the current one, or a closed one the character has not fought since) and the
        // kept standings of the characters that moved on (rank-duel-season). A character is in one of them only, as
        // a record moves to a later season once. The highest rating first, then the higher best, then the character
        // id.
        private List<RankDuelRow> RankDuelRows(int season) {
            List<RankDuelRow> current = (from record in Context.RankDuelRecord.AsNoTracking()
                                         join character in Context.Character.AsNoTracking() on record.CharacterId equals character.Id
                                         where record.Season == season
                                         select new RankDuelRow {
                                             CharacterId = record.CharacterId,
                                             Rating = record.Rating,
                                             BestRating = record.BestRating,
                                             Name = character.Name,
                                             Profile = character.Profile,
                                         }).ToList();
            List<RankDuelRow> closed = (from record in Context.RankDuelSeasonRecord.AsNoTracking()
                                        join character in Context.Character.AsNoTracking() on record.CharacterId equals character.Id
                                        where record.Season == season
                                        select new RankDuelRow {
                                            CharacterId = record.CharacterId,
                                            Rating = record.Rating,
                                            BestRating = record.BestRating,
                                            Name = character.Name,
                                            Profile = character.Profile,
                                        }).ToList();
            return current.Concat(closed)
                .OrderByDescending(row => row.Rating)
                .ThenByDescending(row => row.BestRating)
                .ThenBy(row => row.CharacterId)
                .ToList();
        }

        private static RankDuelRankInfo ToRankInfo(RankDuelRow row, int rank) {
            return new RankDuelRankInfo(rank, row.CharacterId, row.Name, row.Profile.Picture ?? string.Empty, row.Rating, row.BestRating);
        }

        public IList<RankDuelRankInfo> GetRankDuelRankings(int season) {
            return RankDuelRows(season)
                .Take(200)
                .Select((row, index) => ToRankInfo(row, index + 1))
                .ToList();
        }

        public RankDuelRankInfo? GetRankDuelRankInfo(int season, long characterId) {
            List<RankDuelRow> rows = RankDuelRows(season);
            int index = rows.FindIndex(row => row.CharacterId == characterId);
            return index < 0 ? null : ToRankInfo(rows[index], index + 1);
        }

        // The search by name finds the character as the trophy search does (GetCharacterId, any case).
        public RankDuelRankInfo? GetRankDuelRankInfo(int season, string name) {
            long characterId = GetCharacterId(name);
            return characterId == default ? null : GetRankDuelRankInfo(season, characterId);
        }

        private class RankDuelRow {
            public long CharacterId { get; init; }
            public int Rating { get; init; }
            public int BestRating { get; init; }
            public string Name { get; init; } = string.Empty;
            public Model.Profile Profile { get; init; } = null!;
        }
    }
}
