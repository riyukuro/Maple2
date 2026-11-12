using Maple2.Database.Extensions;
using Maple2.Model.Game.Dungeon;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Database.Storage;

public partial class GameStorage {
    public partial class Request {
        public ArcadeBest? GetArcadeBest(long characterId, int arcadeId, int seasonId) {
            Model.ArcadeRecord? record = Context.ArcadeRecord.AsNoTracking()
                .FirstOrDefault(row => row.CharacterId == characterId && row.ArcadeId == arcadeId && row.SeasonId == seasonId);
            return record == null ? null : new ArcadeBest(record.ArcadeId, record.Score, record.SetTime.ToEpochSeconds());
        }

        // The character's best score of each arcade game in the season, by arcade id.
        public IList<ArcadeBest> GetArcadeBests(long characterId, int seasonId) {
            return Context.ArcadeRecord.AsNoTracking()
                .Where(row => row.CharacterId == characterId && row.SeasonId == seasonId)
                .OrderBy(row => row.ArcadeId)
                .AsEnumerable()
                .Select(row => new ArcadeBest(row.ArcadeId, row.Score, row.SetTime.ToEpochSeconds()))
                .ToList();
        }

        // Keeps the score as the character's best of the game and season when it replaces it (ArcadeBest.Replaces).
        // True when it was saved.
        public bool SaveArcadeScore(long characterId, int arcadeId, int seasonId, int score, long time) {
            Context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;
            if (!Context.Character.Any(character => character.Id == characterId)) {
                return false;
            }

            Model.ArcadeRecord? record = Context.ArcadeRecord
                .FirstOrDefault(row => row.CharacterId == characterId && row.ArcadeId == arcadeId && row.SeasonId == seasonId);
            ArcadeBest? best = record == null ? null : new ArcadeBest(arcadeId, record.Score, record.SetTime.ToEpochSeconds());
            if (!ArcadeBest.Replaces(best, score)) {
                return false;
            }

            if (record == null) {
                Context.ArcadeRecord.Add(new Model.ArcadeRecord {
                    CharacterId = characterId,
                    ArcadeId = arcadeId,
                    SeasonId = seasonId,
                    Score = score,
                    SetTime = time.FromEpochSeconds(),
                });
            } else {
                record.Score = score;
                record.SetTime = time.FromEpochSeconds();
                Context.ArcadeRecord.Update(record);
            }
            return Context.TrySaveChanges();
        }
    }
}
