using Maple2.Database.Extensions;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Achievement = Maple2.Model.Game.Achievement;

namespace Maple2.Database.Storage;

public partial class GameStorage {
    public partial class Request {
        public Achievement? CreateAchievement(long ownerId, Achievement achievement) {
            Model.Achievement model = achievement;
            model.OwnerId = ownerId;
            Context.Achievement.Add(model);

            return Context.TrySaveChanges() ? ToAchievement(model) : null;
        }

        public IDictionary<int, Achievement> GetAchievements(long ownerId) {
            return Context.Achievement.Where(achievement => achievement.OwnerId == ownerId)
                .AsEnumerable()
                .Select(ToAchievement)
                .Where(achievement => achievement != null)
                .ToDictionary(achievement => achievement!.Id, achievement => achievement!);
        }

        // The trophy totals of a character with its account's trophies, as the client tallies them
        // (AchievementInfo.Add): by the trophy's metadata, so a stored row counts by its id whatever category it was
        // stored with. A trophy without metadata counts nothing.
        public AchievementInfo GetAchievementInfo(long accountId, long characterId) {
            var rows = Context.Achievement
                .Where(achievement => achievement.OwnerId == accountId || achievement.OwnerId == characterId)
                .Select(achievement => new { achievement.Id, achievement.CompletedCount })
                .ToList();

            var info = new AchievementInfo();
            foreach (var row in rows) {
                if (game.achievementMetadata.TryGet(row.Id, out AchievementMetadata? metadata)) {
                    info.Add(metadata, row.CompletedCount);
                }
            }
            return info;
        }

        public bool SaveAchievements(long ownerId, ICollection<Achievement> achievements) {
            foreach (Achievement achievement in achievements) {
                Model.Achievement model = achievement;
                model.OwnerId = ownerId;

                Context.Achievement.Update(model);
            }

            return Context.TrySaveChanges();
        }

        // Converts model to item if possible, otherwise returns null.
        private Achievement? ToAchievement(Model.Achievement? model) {
            if (model == null) {
                return null;
            }

            return game.achievementMetadata.TryGet(model.Id, out AchievementMetadata? metadata) ? model.Convert(metadata) : null;
        }
    }
}
