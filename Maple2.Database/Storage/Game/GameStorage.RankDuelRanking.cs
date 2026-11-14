using Maple2.Database.Model.Ranking;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Database.Storage;

public partial class GameStorage {
    public partial class Request {
        // The standings of a PvP season, read with their characters (a row whose character is gone is not shown): the
        // records still in that season (the current one, or a closed one the character has not fought since) and the
        // kept standings of the characters that moved on (rank-duel-season). A character is in one of them only, as
        // a record moves to a later season once.
        private IQueryable<RankDuelRow> CurrentRankDuelRows(int season) {
            return from record in Context.RankDuelRecord.AsNoTracking()
                   join character in Context.Character.AsNoTracking() on record.CharacterId equals character.Id
                   where record.Season == season
                   select new RankDuelRow {
                       CharacterId = record.CharacterId,
                       Rating = record.Rating,
                       BestRating = record.BestRating,
                       Name = character.Name,
                       Profile = character.Profile,
                   };
        }

        private IQueryable<RankDuelRow> ClosedRankDuelRows(int season) {
            return from record in Context.RankDuelSeasonRecord.AsNoTracking()
                   join character in Context.Character.AsNoTracking() on record.CharacterId equals character.Id
                   where record.Season == season
                   select new RankDuelRow {
                       CharacterId = record.CharacterId,
                       Rating = record.Rating,
                       BestRating = record.BestRating,
                       Name = character.Name,
                       Profile = character.Profile,
                   };
        }

        // The highest rating first, then the higher best, then the character id.
        private static IOrderedEnumerable<RankDuelRow> InRankDuelOrder(IEnumerable<RankDuelRow> rows) {
            return rows.OrderByDescending(row => row.Rating)
                .ThenByDescending(row => row.BestRating)
                .ThenBy(row => row.CharacterId);
        }

        private static IQueryable<RankDuelRow> InRankDuelOrder(IQueryable<RankDuelRow> rows) {
            return rows.OrderByDescending(row => row.Rating)
                .ThenByDescending(row => row.BestRating)
                .ThenBy(row => row.CharacterId);
        }

        // The rows of a source that come before a row in the board order.
        private static int RankDuelBefore(IQueryable<RankDuelRow> rows, RankDuelRow row) {
            return rows.Count(other => other.Rating > row.Rating
                || (other.Rating == row.Rating && (other.BestRating > row.BestRating
                    || (other.BestRating == row.BestRating && other.CharacterId < row.CharacterId))));
        }

        private static RankDuelRankInfo ToRankInfo(RankDuelRow row, int rank) {
            return new RankDuelRankInfo(rank, row.CharacterId, row.Name, row.Profile.Picture ?? string.Empty, row.Rating, row.BestRating);
        }

        // The first 200 of the board: the first 200 of each source, merged.
        public IList<RankDuelRankInfo> GetRankDuelRankings(int season) {
            List<RankDuelRow> current = InRankDuelOrder(CurrentRankDuelRows(season)).Take(200).ToList();
            List<RankDuelRow> closed = InRankDuelOrder(ClosedRankDuelRows(season)).Take(200).ToList();
            return InRankDuelOrder(current.Concat(closed))
                .Take(200)
                .Select((row, index) => ToRankInfo(row, index + 1))
                .ToList();
        }

        // The rank of a character: 1 + the rows of both sources that come before its row.
        public RankDuelRankInfo? GetRankDuelRankInfo(int season, long characterId) {
            RankDuelRow? row = CurrentRankDuelRows(season).FirstOrDefault(row => row.CharacterId == characterId)
                ?? ClosedRankDuelRows(season).FirstOrDefault(row => row.CharacterId == characterId);
            if (row == null) {
                return null;
            }
            int rank = 1 + RankDuelBefore(CurrentRankDuelRows(season), row) + RankDuelBefore(ClosedRankDuelRows(season), row);
            return ToRankInfo(row, rank);
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
