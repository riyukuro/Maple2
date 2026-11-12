using Maple2.Database.Extensions;
using Maple2.Model.Game.Pvp;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Database.Storage;

public partial class GameStorage {
    public partial class Request {
        // The character's rank duel record; a character with no row has the start record (no season yet).
        public RankDuelRecord GetRankDuelRecord(long characterId) {
            Model.RankDuelRecord? row = Context.RankDuelRecord.AsNoTracking().FirstOrDefault(record => record.CharacterId == characterId);
            var record = new RankDuelRecord();
            if (row != null) {
                record.Season = row.Season;
                record.Rating = row.Rating;
                record.BestRating = row.BestRating;
                record.WeeklyReward = row.WeeklyReward;
            }
            foreach (Model.RankDuelJobRecord job in Context.RankDuelJobRecord.AsNoTracking().Where(job => job.CharacterId == characterId)) {
                record.Jobs[job.JobCode] = new Maple2.Model.Game.Pvp.RankDuelJobRecord { Wins = job.Wins, Losses = job.Losses };
            }
            return record;
        }

        // Writes the whole record: the standing and the job rows (a job row that is no longer there goes).
        public bool SaveRankDuelRecord(long characterId, RankDuelRecord record) {
            Context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;
            if (!Context.Character.Any(character => character.Id == characterId)) {
                return false;
            }

            Model.RankDuelRecord? row = Context.RankDuelRecord.FirstOrDefault(r => r.CharacterId == characterId);
            if (row == null) {
                row = new Model.RankDuelRecord { CharacterId = characterId };
                Context.RankDuelRecord.Add(row);
            }
            row.Season = record.Season;
            row.Rating = record.Rating;
            row.BestRating = record.BestRating;
            row.WeeklyReward = record.WeeklyReward;

            List<Model.RankDuelJobRecord> jobs = Context.RankDuelJobRecord.Where(job => job.CharacterId == characterId).ToList();
            foreach (Model.RankDuelJobRecord job in jobs.Where(job => !record.Jobs.ContainsKey(job.JobCode))) {
                Context.RankDuelJobRecord.Remove(job);
            }
            foreach ((int jobCode, Maple2.Model.Game.Pvp.RankDuelJobRecord value) in record.Jobs) {
                Model.RankDuelJobRecord? job = jobs.FirstOrDefault(j => j.JobCode == jobCode);
                if (job == null) {
                    Context.RankDuelJobRecord.Add(new Model.RankDuelJobRecord { CharacterId = characterId, JobCode = jobCode, Wins = value.Wins, Losses = value.Losses });
                } else {
                    job.Wins = value.Wins;
                    job.Losses = value.Losses;
                }
            }
            return Context.TrySaveChanges();
        }
    }
}
