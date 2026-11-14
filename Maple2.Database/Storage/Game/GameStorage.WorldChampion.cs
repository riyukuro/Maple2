using Maple2.Database.Extensions;
using Maple2.Model.Game;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Database.Storage;

public partial class GameStorage {
    public partial class Request {
        // The season's step and the damage dealt to it, or null before the season's first save.
        public (int Step, long StepDamage)? GetWorldChampionSeason(int season) {
            Model.WorldChampionSeason? row = Context.WorldChampionSeason.AsNoTracking().FirstOrDefault(row => row.Season == season);
            return row == null ? null : (row.Step, row.StepDamage);
        }

        public List<WorldChampionDamage> GetWorldChampionDamages(int season) {
            return Context.WorldChampionDamage.AsNoTracking()
                .Where(row => row.Season == season)
                .Select(row => new WorldChampionDamage(row.AccountId, row.CharacterId, row.Damage))
                .ToList();
        }

        // Writes the season's step and its characters' damage (the totals, not the change). A character that is gone
        // is skipped; the reward mark of a row is kept.
        public bool SaveWorldChampion(int season, int step, long stepDamage, IReadOnlyCollection<WorldChampionDamage> damages) {
            Context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;
            Model.WorldChampionSeason? row = Context.WorldChampionSeason.FirstOrDefault(row => row.Season == season);
            if (row == null) {
                row = new Model.WorldChampionSeason { Season = season };
                Context.WorldChampionSeason.Add(row);
            }
            row.Step = step;
            row.StepDamage = stepDamage;

            if (damages.Count > 0) {
                var characterIds = damages.Select(damage => damage.CharacterId).ToHashSet();
                var existing = Context.Character.Where(character => characterIds.Contains(character.Id))
                    .Select(character => character.Id)
                    .ToHashSet();
                Dictionary<long, Model.WorldChampionDamage> rows = Context.WorldChampionDamage
                    .Where(damage => damage.Season == season && characterIds.Contains(damage.CharacterId))
                    .ToDictionary(damage => damage.CharacterId);
                foreach (WorldChampionDamage damage in damages) {
                    if (!existing.Contains(damage.CharacterId)) {
                        continue;
                    }
                    if (!rows.TryGetValue(damage.CharacterId, out Model.WorldChampionDamage? damageRow)) {
                        damageRow = new Model.WorldChampionDamage { Season = season, CharacterId = damage.CharacterId };
                        Context.WorldChampionDamage.Add(damageRow);
                        rows[damage.CharacterId] = damageRow;
                    }
                    damageRow.AccountId = damage.AccountId;
                    damageRow.Damage = damage.Damage;
                }
            }
            bool saved = Context.TrySaveChanges();
            if (!saved) {
                Context.ChangeTracker.Clear();
            }
            return saved;
        }

        // The character's reward mail of the season, with the reward claim (ClaimWorldChampionReward) in the same
        // transaction: the mail is made once, and a claim is kept only with its mail. Null when the character is not
        // due the reward (not enough damage, or rewarded) or a write failed.
        public Mail? MailWorldChampionReward(int season, long characterId, long minDamage, Mail mail) {
            Context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;
            BeginTransaction();
            Model.WorldChampionDamage? row = Context.WorldChampionDamage.FirstOrDefault(row =>
                row.Season == season && row.CharacterId == characterId);
            if (row == null || row.Rewarded || row.Damage <= 0 || row.Damage < minDamage) {
                Rollback();
                return null;
            }
            row.Rewarded = true;
            mail.SetDefaultExpiry(game.serverTableMetadata.ConstantsTable.MailExpiryDays, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            Model.Mail model = mail;
            model.Id = 0;
            Context.Mail.Add(model);
            if (!SaveChanges()) {
                Rollback();
                return null;
            }
            // The items are new (CreateItem's rows), made in the mail.
            var itemRows = new List<Model.Item>();
            foreach (Item item in mail.Items) {
                Model.Item itemRow = item;
                itemRow.OwnerId = model.Id;
                itemRow.Id = 0;
                Context.Item.Add(itemRow);
                itemRows.Add(itemRow);
            }
            if (itemRows.Count > 0 && !SaveChanges()) {
                Rollback();
                return null;
            }
            if (!Commit()) {
                return null;
            }
            Mail saved = model;
            foreach (Model.Item itemRow in itemRows) {
                if (ToItem(itemRow) is { } added) {
                    saved.Items.Add(added);
                }
            }
            return saved;
        }

        // Marks the character's reward of the season sent when it dealt the damage and was not rewarded yet: true
        // once per character and season, so a reward mail is sent once.
        public bool ClaimWorldChampionReward(int season, long characterId, long minDamage) {
            Context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;
            Model.WorldChampionDamage? row = Context.WorldChampionDamage.FirstOrDefault(row =>
                row.Season == season && row.CharacterId == characterId);
            if (row == null || row.Rewarded || row.Damage <= 0 || row.Damage < minDamage) {
                return false;
            }
            row.Rewarded = true;
            bool saved = Context.TrySaveChanges();
            if (!saved) {
                Context.ChangeTracker.Clear();
            }
            return saved;
        }
    }
}
