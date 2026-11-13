using Maple2.Database.Extensions;
using Maple2.Database.Model.Ranking;
using Maple2.Model;
using Maple2.Model.Enum;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Database.Storage;

public partial class GameStorage {
    public partial class Request {
        // The rows of an arcade game's board in a season, read with their characters, so a row whose character is
        // gone is not shown (the database removes it with the character).
        private IQueryable<ArcadeRow> ArcadeRows(int arcadeId, int seasonId) {
            return from record in Context.ArcadeRecord.AsNoTracking()
                   join character in Context.Character.AsNoTracking() on record.CharacterId equals character.Id
                   where record.ArcadeId == arcadeId && record.SeasonId == seasonId
                   select new ArcadeRow {
                       CharacterId = record.CharacterId,
                       Score = record.Score,
                       SetTime = record.SetTime,
                       Name = character.Name,
                       Job = character.Job,
                       Level = character.Level,
                       Profile = character.Profile,
                   };
        }

        // The highest score first; equal scores rank by the time they were set, the earlier one first, then by the
        // character id.
        private static IQueryable<ArcadeRow> InArcadeOrder(IQueryable<ArcadeRow> rows) {
            return rows.OrderByDescending(row => row.Score)
                .ThenBy(row => row.SetTime)
                .ThenBy(row => row.CharacterId);
        }

        private static ArcadeRankInfo ToRankInfo(ArcadeRow row, int arcadeId, int seasonId, int rank) {
            return new ArcadeRankInfo(
                Rank: rank,
                CharacterId: row.CharacterId,
                Name: row.Name,
                Profile: row.Profile.Picture ?? string.Empty,
                ArcadeId: arcadeId,
                SeasonId: seasonId,
                JobCode: (int) row.Job.Code(),
                Level: row.Level,
                Score: row.Score,
                SetTime: row.SetTime.ToEpochSeconds());
        }

        // The rank of a row: 1 + the rows of the board that come before it in the board order.
        private int ArcadeRank(int arcadeId, int seasonId, ArcadeRow row) {
            return 1 + ArcadeRows(arcadeId, seasonId).Count(other => other.Score > row.Score
                || (other.Score == row.Score && (other.SetTime < row.SetTime
                    || (other.SetTime == row.SetTime && other.CharacterId < row.CharacterId))));
        }

        public IList<ArcadeRankInfo> GetArcadeRankings(int arcadeId, int seasonId) {
            return InArcadeOrder(ArcadeRows(arcadeId, seasonId))
                .Take(200)
                .AsEnumerable()
                .Select((row, index) => ToRankInfo(row, arcadeId, seasonId, index + 1))
                .ToList();
        }

        public ArcadeRankInfo? GetArcadeRankInfo(int arcadeId, int seasonId, long characterId) {
            ArcadeRow? row = ArcadeRows(arcadeId, seasonId).FirstOrDefault(row => row.CharacterId == characterId);
            return row == null ? null : ToRankInfo(row, arcadeId, seasonId, ArcadeRank(arcadeId, seasonId, row));
        }

        // The search by name finds the character as the trophy search does (GetCharacterId, any case).
        public ArcadeRankInfo? GetArcadeRankInfo(int arcadeId, int seasonId, string name) {
            long characterId = GetCharacterId(name);
            return characterId == default ? null : GetArcadeRankInfo(arcadeId, seasonId, characterId);
        }

        private class ArcadeRow {
            public long CharacterId { get; init; }
            public int Score { get; init; }
            public DateTime SetTime { get; init; }
            public string Name { get; init; } = string.Empty;
            public Job Job { get; init; }
            public short Level { get; init; }
            public Model.Profile Profile { get; init; } = null!;
        }
    }
}
