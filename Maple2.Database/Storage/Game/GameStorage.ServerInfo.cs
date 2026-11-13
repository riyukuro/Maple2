using Maple2.Database.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace Maple2.Database.Storage;

public partial class GameStorage {
    // The times of the last daily, weekly and monthly reset the World stored (server-info).
    public readonly record struct ResetStamps(DateTime Daily, DateTime Weekly, DateTime Monthly);

    public partial class Request {
        public DateTime GetLastDailyReset() {
            ServerInfo? dailyReset = Context.ServerInfo.Find("DailyReset");
            return dailyReset?.LastModified ?? CreateServerInfo("DailyReset");
        }

        public DateTime GetLastWeeklyReset() {
            ServerInfo? weeklyReset = Context.ServerInfo.Find("WeeklyReset");
            return weeklyReset?.LastModified ?? CreateServerInfo("WeeklyReset");
        }

        public DateTime GetLastMonthlyReset() {
            ServerInfo? monthlyReset = Context.ServerInfo.Find("MonthlyReset");
            return monthlyReset?.LastModified ?? CreateServerInfo("MonthlyReset");
        }

        private DateTime CreateServerInfo(string key) {
            var model = new ServerInfo {
                Key = key,
                LastModified = DateTime.Now,
            };
            Context.ServerInfo.Add(model);
            Context.SaveChanges();

            return model.LastModified;
        }

        // The times of the last daily, weekly and monthly reset (DateTime.MinValue for one never run). In an open
        // transaction on a relational store the rows are read with a share lock: a reset (RunReset, its server-info
        // row first) cannot commit until this transaction ends, so the rows this transaction reads or writes hold
        // every reset up to these times and none after them.
        public ResetStamps GetResetStamps() {
            List<ServerInfo> rows = IsTransaction && Context.Database.IsRelational()
                ? Context.ServerInfo.FromSqlRaw("SELECT * FROM `server-info` LOCK IN SHARE MODE").AsNoTracking().ToList()
                : Context.ServerInfo.AsNoTracking().ToList();
            return new ResetStamps(Stamp("DailyReset"), Stamp("WeeklyReset"), Stamp("MonthlyReset"));

            DateTime Stamp(string key) => rows.FirstOrDefault(row => row.Key == key)?.LastModified ?? DateTime.MinValue;
        }

        public void DailyReset() {
            RunReset("DailyReset", () => {
                Context.Database.ExecuteSqlRaw("UPDATE `account` SET `PrestigeExp` = `PrestigeCurrentExp`");
                Context.Database.ExecuteSqlRaw("UPDATE `account` SET `PrestigeLevelsGained` = DEFAULT");
                Context.Database.ExecuteSqlRaw("UPDATE `account` SET `PremiumRewardsClaimed` = DEFAULT");
                Context.Database.ExecuteSqlRaw("UPDATE `character-config` SET `GatheringCounts` = DEFAULT");
                Context.Database.ExecuteSqlRaw("UPDATE `character-config` SET `InstantRevivalCount` = 0");
                Context.Database.ExecuteSqlRaw("UPDATE `nurturing` SET `PlayedBy` = '[]'");
                Context.Database.ExecuteSqlRaw("DELETE FROM `nurturing-daily`");
                Context.Database.ExecuteSqlRaw("UPDATE `home` SET `DecorationRewardTimestamp` = 0");
                Context.Database.ExecuteSqlRaw("UPDATE `character-shop-data` SET `RestockCount` = 0 WHERE `Interval` = 1");
                // Reset shop item stock purchased for daily shops
                Context.Database.ExecuteSqlRaw("UPDATE `character-shop-item-data` SET `StockPurchased` = 0 WHERE `ShopId` IN (SELECT `ShopId` FROM `character-shop-data` WHERE `Interval` = 1)");
                Context.Database.ExecuteSqlRaw("UPDATE `account` SET `MarketLimits` = JSON_SET(MarketLimits, '$.MesoListed', 0)");
                // Dungeon daily clears
                // Bought extra counts (ExtraCurrentSubClears) last the record's period and event counts their own, as online.
                Context.Database.ExecuteSqlRaw("UPDATE `dungeon-record` SET `CurrentSubClears` = 0");
            });
        }

        public void WeeklyReset() {
            RunReset("WeeklyReset", () => {
                // The guild raid records go with the weekly contribution (GuildRules.ResetRaids; Chosen here).
                Context.Database.ExecuteSqlRaw("UPDATE `guild-member` SET `WeeklyContribution` = 0, `Raids` = NULL");
                // The rank duel's weekly valor tokens (the dialog's gauge against weeklyRewardMax).
                Context.Database.ExecuteSqlRaw("UPDATE `rank-duel-record` SET `WeeklyReward` = 0");
                Context.Database.ExecuteSqlRaw("UPDATE `account` SET `PrestigeRewardsClaimed` = DEFAULT");
                Context.Database.ExecuteSqlRaw("UPDATE `character-shop-data` SET `RestockCount` = 0 WHERE `Interval` = 2");
                // Reset shop item stock purchased for weekly shops
                Context.Database.ExecuteSqlRaw("UPDATE `character-shop-item-data` SET `StockPurchased` = 0 WHERE `ShopId` IN (SELECT `ShopId` FROM `character-shop-data` WHERE `Interval` = 2)");
                // Dungeon weekly clears, as DungeonManager.ResetWeeklyClears: a record whose own period is over (its
                // UnionCooldownTime) loses its clears and bought extra counts; event counts keep their own expiry.
                Context.Database.ExecuteSqlRaw("UPDATE `dungeon-record` SET `CurrentClears` = 0, `ExtraCurrentSubClears` = 0 WHERE `UnionCooldownTime` < {0}", DateTime.Now);
            });
        }

        public void MonthlyReset() {
            RunReset("MonthlyReset", () => {
                Context.Database.ExecuteSqlRaw("UPDATE `account` SET `MarketLimits` = JSON_SET(MarketLimits, '$.MesoPurchased', 0)");
            });
        }

        // A reset's rows and its server-info time are stored in one transaction, the time first: a session's save
        // reads the time with a share lock before it writes (GetResetStamps), so it either waits for the whole reset
        // and takes it in memory before writing, or is stored before the reset, which then resets what it wrote. The
        // statements used to be stored one by one after the time, so a save between them and the channels' reset
        // wrote the old values back, and a session that ended then kept them. A reset that fails (a deadlock with a
        // save) is rolled back whole and tried again, three tries in all.
        private void RunReset(string key, Action statements) {
            lock (Context) {
                for (int tries = 1; ; tries++) {
                    try {
                        using IDbContextTransaction transaction = Context.Database.BeginTransaction();
                        Context.Database.ExecuteSqlRaw("INSERT INTO `server-info` (`Key`, `LastModified`) VALUES ({0}, {1}) ON DUPLICATE KEY UPDATE `LastModified` = VALUES(`LastModified`)",
                            key, DateTime.Now);
                        statements();
                        transaction.Commit();
                        return;
                    } catch (Exception ex) when (tries < 3) {
                        Logger.LogWarning(ex, "{Key}: the reset was rolled back, tried again", key);
                    }
                }
            }
        }
    }
}
