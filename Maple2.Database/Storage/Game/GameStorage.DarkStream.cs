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

        // The whole board, best first; a job code other than 0 keeps the characters of that job (the client's job
        // tabs). Equal scores rank by the time they were set, the earlier one first.
        private List<DarkStreamRankInfo> DarkStreamBoard(int jobCode) {
            var rows = (from record in Context.DarkStreamRecord.AsNoTracking()
                        join character in Context.Character.AsNoTracking() on record.CharacterId equals character.Id
                        select new {
                            record.CharacterId,
                            record.Score,
                            record.SetTime,
                            character.Name,
                            character.Job,
                            Picture = character.Profile.Picture,
                        })
                .AsEnumerable()
                .Where(row => jobCode == 0 || (int) row.Job.Code() == jobCode)
                .OrderByDescending(row => row.Score)
                .ThenBy(row => row.SetTime)
                .ThenBy(row => row.CharacterId);
            return rows.Select((row, index) => new DarkStreamRankInfo(
                    Rank: index + 1,
                    CharacterId: row.CharacterId,
                    Name: row.Name,
                    Profile: row.Picture ?? string.Empty,
                    Score: row.Score,
                    SetTime: row.SetTime.ToEpochSeconds()))
                .ToList();
        }

        public IList<DarkStreamRankInfo> GetDarkStreamRankings(int jobCode) {
            return DarkStreamBoard(jobCode).Take(200).ToList();
        }

        public DarkStreamRankInfo? GetDarkStreamRankInfo(int jobCode, long characterId) {
            return DarkStreamBoard(jobCode).FirstOrDefault(row => row.CharacterId == characterId);
        }

        public DarkStreamRankInfo? GetDarkStreamRankInfo(int jobCode, string name) {
            return DarkStreamBoard(jobCode).FirstOrDefault(row => row.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }
    }
}
