using Maple2.Database.Extensions;
using Maple2.Database.Model.Ranking;
using Maple2.Model;
using Maple2.Model.Enum;
using Maple2.Model.Game.Dungeon;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Database.Storage;

public partial class GameStorage {
    public partial class Request {
        // The rows of the Dark Stream board are read with their characters, so a row whose character is gone
        // is not shown (the database removes it with the character).
        public DarkStreamBest? GetDarkStreamBest(long characterId) {
            Model.DarkStreamRecord? record = Context.DarkStreamRecord.AsNoTracking()
                .FirstOrDefault(row => row.CharacterId == characterId);
            return record == null ? null : new DarkStreamBest(record.Score, record.SetTime.ToEpochSeconds());
        }

        // Keeps the score when it replaces the character's best (DarkStreamBest.Replaces). True when it was saved.
        public bool SaveDarkStreamScore(long characterId, int score, long time) {
            Context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;
            if (!Context.Character.Any(character => character.Id == characterId)) {
                return false;
            }

            Model.DarkStreamRecord? record = Context.DarkStreamRecord.FirstOrDefault(row => row.CharacterId == characterId);
            DarkStreamBest? best = record == null ? null : new DarkStreamBest(record.Score, record.SetTime.ToEpochSeconds());
            if (!DarkStreamBest.Replaces(best, score)) {
                return false;
            }

            if (record == null) {
                Context.DarkStreamRecord.Add(new Model.DarkStreamRecord {
                    CharacterId = characterId,
                    Score = score,
                    SetTime = time.FromEpochSeconds(),
                });
            } else {
                record.Score = score;
                record.SetTime = time.FromEpochSeconds();
                Context.DarkStreamRecord.Update(record);
            }
            return Context.TrySaveChanges();
        }

        // The rows of a job tab with their characters, in the database: a job code other than 0 keeps the characters
        // of that job (the client's job tabs; Job.Code() is the job / 10, and the game master job is its own code).
        private IQueryable<DarkStreamRow> DarkStreamRows(int jobCode) {
            IQueryable<DarkStreamRow> rows = from record in Context.DarkStreamRecord.AsNoTracking()
                                             join character in Context.Character.AsNoTracking() on record.CharacterId equals character.Id
                                             select new DarkStreamRow {
                                                 CharacterId = record.CharacterId,
                                                 Score = record.Score,
                                                 SetTime = record.SetTime,
                                                 Name = character.Name,
                                                 Job = character.Job,
                                                 Profile = character.Profile,
                                             };
            if (jobCode == (int) JobCode.GameMaster) {
                return rows.Where(row => row.Job == Job.GameMaster);
            }
            if (jobCode != 0) {
                var low = (Job) (jobCode * 10);
                var high = (Job) (jobCode * 10 + 9);
                return rows.Where(row => row.Job != Job.GameMaster && row.Job >= low && row.Job <= high);
            }
            return rows;
        }

        // Best first; equal scores rank by the time they were set, the earlier one first, then by the character id.
        private static IQueryable<DarkStreamRow> InBoardOrder(IQueryable<DarkStreamRow> rows) {
            return rows.OrderByDescending(row => row.Score)
                .ThenBy(row => row.SetTime)
                .ThenBy(row => row.CharacterId);
        }

        private static DarkStreamRankInfo ToRankInfo(DarkStreamRow row, int rank) {
            return new DarkStreamRankInfo(
                Rank: rank,
                CharacterId: row.CharacterId,
                Name: row.Name,
                Profile: row.Profile.Picture ?? string.Empty,
                Score: row.Score,
                SetTime: row.SetTime.ToEpochSeconds());
        }

        // The rank of a row: 1 + the rows of the same tab that come before it in the board order.
        private int DarkStreamRank(int jobCode, DarkStreamRow row) {
            return 1 + DarkStreamRows(jobCode).Count(other => other.Score > row.Score
                || (other.Score == row.Score && (other.SetTime < row.SetTime
                    || (other.SetTime == row.SetTime && other.CharacterId < row.CharacterId))));
        }

        public IList<DarkStreamRankInfo> GetDarkStreamRankings(int jobCode) {
            return InBoardOrder(DarkStreamRows(jobCode))
                .Take(200)
                .AsEnumerable()
                .Select((row, index) => ToRankInfo(row, index + 1))
                .ToList();
        }

        public DarkStreamRankInfo? GetDarkStreamRankInfo(int jobCode, long characterId) {
            DarkStreamRow? row = DarkStreamRows(jobCode).FirstOrDefault(row => row.CharacterId == characterId);
            return row == null ? null : ToRankInfo(row, DarkStreamRank(jobCode, row));
        }

        // The search by name finds the character as the trophy search does (GetCharacterId, any case).
        public DarkStreamRankInfo? GetDarkStreamRankInfo(int jobCode, string name) {
            long characterId = GetCharacterId(name);
            return characterId == default ? null : GetDarkStreamRankInfo(jobCode, characterId);
        }

        private class DarkStreamRow {
            public long CharacterId { get; init; }
            public int Score { get; init; }
            public DateTime SetTime { get; init; }
            public string Name { get; init; } = string.Empty;
            public Job Job { get; init; }
            public Model.Profile Profile { get; init; } = null!;
        }
    }
}
