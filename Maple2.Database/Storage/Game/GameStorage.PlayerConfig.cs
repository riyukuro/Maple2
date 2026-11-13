using Maple2.Database.Extensions;
using Maple2.Model.Game;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Database.Storage;

public partial class GameStorage {
    public partial class Request {
        // The player config the World last saved for the character; null when it has none.
        public PlayerConfigSave? GetPlayerConfig(long characterId) {
            Model.CharacterPlayerConfig? row = Context.CharacterPlayerConfig.AsNoTracking()
                .FirstOrDefault(config => config.CharacterId == characterId);
            if (row == null) {
                return null;
            }
            return new PlayerConfigSave {
                Buffs = row.Buffs,
                SkillCooldowns = row.SkillCooldowns,
                PvpHeldCooldowns = row.PvpHeldCooldowns,
                PvpStay = row.PvpStay,
                DeathCount = row.DeathCount,
                DeathMsRemaining = row.DeathMsRemaining,
                DeathStopTime = row.DeathStopTime,
            };
        }

        // Writes the whole player config of the character, replacing the one before. False for a character that is
        // gone or a failed save.
        public bool SavePlayerConfig(long characterId, PlayerConfigSave save) {
            Context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;
            Model.CharacterPlayerConfig? row = Context.CharacterPlayerConfig.FirstOrDefault(config => config.CharacterId == characterId);
            if (row == null) {
                if (!Context.Character.Any(character => character.Id == characterId)) {
                    return false;
                }
                row = new Model.CharacterPlayerConfig { CharacterId = characterId };
                Context.CharacterPlayerConfig.Add(row);
            }
            row.Buffs = save.Buffs.ToList();
            row.SkillCooldowns = save.SkillCooldowns.ToList();
            row.PvpHeldCooldowns = save.PvpHeldCooldowns.ToList();
            row.PvpStay = save.PvpStay;
            row.DeathCount = save.DeathCount;
            row.DeathMsRemaining = save.DeathMsRemaining;
            row.DeathStopTime = save.DeathStopTime;
            row.LastModified = DateTime.Now;
            return Context.TrySaveChanges();
        }
    }
}
