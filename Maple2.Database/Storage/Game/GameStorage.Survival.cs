using Maple2.Database.Extensions;
using Maple2.Model.Game.Pvp;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Database.Storage;

public partial class GameStorage {
    public partial class Request {
        // The character's record of the season; a new one (SurvivalRules.StartRating) when it has none.
        public SurvivalSeasonRecord GetSurvivalRecord(long characterId, int seasonId) {
            Model.SurvivalRecord? row = Context.SurvivalRecord.AsNoTracking()
                .FirstOrDefault(record => record.CharacterId == characterId && record.SeasonId == seasonId);
            return row == null
                ? new SurvivalSeasonRecord { SeasonId = seasonId, Rating = SurvivalRules.StartRating }
                : new SurvivalSeasonRecord { SeasonId = seasonId, Rating = row.Rating, Games = row.Games, Wins = row.Wins, Kills = row.Kills };
        }

        public bool SaveSurvivalRecord(long characterId, SurvivalSeasonRecord record) {
            Context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;
            if (!Context.Character.Any(character => character.Id == characterId)) {
                return false;
            }
            Model.SurvivalRecord? row = Context.SurvivalRecord
                .FirstOrDefault(entry => entry.CharacterId == characterId && entry.SeasonId == record.SeasonId);
            if (row == null) {
                Context.SurvivalRecord.Add(new Model.SurvivalRecord {
                    CharacterId = characterId,
                    SeasonId = record.SeasonId,
                    Rating = record.Rating,
                    Games = record.Games,
                    Wins = record.Wins,
                    Kills = record.Kills,
                });
            } else {
                row.Rating = record.Rating;
                row.Games = record.Games;
                row.Wins = record.Wins;
                row.Kills = record.Kills;
                Context.SurvivalRecord.Update(row);
            }
            return Context.TrySaveChanges();
        }
    }
}
