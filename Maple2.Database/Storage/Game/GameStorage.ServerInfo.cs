using System.Diagnostics;
using Maple2.Database.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace Maple2.Database.Storage;

// A save's read of the reset times found a reset being stored, and did not wait (or waited its time out).
public sealed class ResetRunningException(string message) : Exception(message);

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

        private static readonly string[] ResetKeys = ["DailyReset", "WeeklyReset", "MonthlyReset"];
        private static readonly TimeSpan ResetPoll = TimeSpan.FromMilliseconds(100);

        // MySQL's ER_LOCK_NOWAIT: the row a NOWAIT read asked for is locked. MySqlConnector's exception is read by its
        // Number (the provider's assembly is not referenced here).
        private static bool IsLockNowait(Exception? ex) {
            for (; ex != null; ex = ex.InnerException) {
                if (ex is System.Data.Common.DbException && ex.GetType().GetProperty("Number")?.GetValue(ex) is 3572) {
                    return true;
                }
            }
            return false;
        }

        // The times of the last daily, weekly and monthly reset (DateTime.MinValue for one never run). In an open
        // transaction on a relational store the three rows are read with a share lock (by their keys, so the lock
        // holds only those rows, not the event-run rows of the table nor its gaps; the World makes the three rows at
        // its start): a reset (RunReset, its server-info row first) cannot commit until this transaction ends, so the
        // rows this transaction reads or writes hold every reset up to these times and none after them.
        // A reset being stored holds its row for its whole run (about 1 to 5 s for a daily reset over 50k characters,
        // up to 30 s before the command times out). The lock is not waited on in the database: the read fails at once
        // (NOWAIT) and is tried again every 100 ms for up to wait, then ResetRunningException. A save that can come
        // back later (the timed save) passes no wait and does not hold its field thread; the others wait for the reset.
        public ResetStamps GetResetStamps(TimeSpan wait = default) {
            List<ServerInfo> rows;
            if (IsTransaction && Context.Database.IsRelational()) {
                var watch = Stopwatch.StartNew();
                while (true) {
                    try {
                        rows = Context.ServerInfo
                            .FromSqlRaw("SELECT * FROM `server-info` WHERE `Key` IN ('DailyReset', 'WeeklyReset', 'MonthlyReset') FOR SHARE NOWAIT")
                            .AsNoTracking().ToList();
                        break;
                    } catch (Exception ex) when (IsLockNowait(ex)) {
                        if (watch.Elapsed >= wait) {
                            throw new ResetRunningException($"a reset is being stored (waited {watch.ElapsedMilliseconds} ms)");
                        }
                        Thread.Sleep(ResetPoll);
                    }
                }
            } else {
                rows = Context.ServerInfo.AsNoTracking().Where(row => ResetKeys.Contains(row.Key)).ToList();
            }
            return new ResetStamps(Stamp("DailyReset"), Stamp("WeeklyReset"), Stamp("MonthlyReset"));

            DateTime Stamp(string key) => rows.FirstOrDefault(row => row.Key == key)?.LastModified ?? DateTime.MinValue;
        }

        // The stored time of one reset (DateTime.MinValue before its first run), read without a lock.
        public DateTime GetResetStamp(string key) {
            return Context.ServerInfo.AsNoTracking().Where(row => row.Key == key).Select(row => row.LastModified).FirstOrDefault();
        }

        public void DailyReset(DateTime? storedBefore = null) {
            RunReset("DailyReset", storedBefore, () => {
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
                Context.Database.ExecuteSqlRaw("UPDATE `dungeon-account-record` SET `CurrentSubClears` = 0");
            });
        }

        public void WeeklyReset(DateTime? storedBefore = null) {
            RunReset("WeeklyReset", storedBefore, () => {
                // The guild raid records go with the weekly contribution (GuildRules.ResetRaids; Chosen here).
                Context.Database.ExecuteSqlRaw("UPDATE `guild-member` SET `WeeklyContribution` = 0, `Raids` = NULL");
                // The rank duel's weekly valor tokens (the dialog's gauge against weeklyRewardMax).
                Context.Database.ExecuteSqlRaw("UPDATE `rank-duel-record` SET `WeeklyReward` = 0");
                Context.Database.ExecuteSqlRaw("UPDATE `character-shop-data` SET `RestockCount` = 0 WHERE `Interval` = 2");
                // Reset shop item stock purchased for weekly shops
                Context.Database.ExecuteSqlRaw("UPDATE `character-shop-item-data` SET `StockPurchased` = 0 WHERE `ShopId` IN (SELECT `ShopId` FROM `character-shop-data` WHERE `Interval` = 2)");
                // Dungeon weekly clears, as DungeonManager.ResetWeeklyClears: a record whose own period is over (its
                // UnionCooldownTime) loses its clears and bought extra counts; event counts keep their own expiry.
                Context.Database.ExecuteSqlRaw("UPDATE `dungeon-record` SET `CurrentClears` = 0, `ExtraCurrentSubClears` = 0 WHERE `UnionCooldownTime` < {0}", DateTime.Now);
                Context.Database.ExecuteSqlRaw("UPDATE `dungeon-account-record` SET `CurrentClears` = 0, `ExtraCurrentSubClears` = 0 WHERE `UnionCooldownTime` < {0}", DateTime.Now);
            });
        }

        public void MonthlyReset(DateTime? storedBefore = null) {
            RunReset("MonthlyReset", storedBefore, () => {
                Context.Database.ExecuteSqlRaw("UPDATE `account` SET `MarketLimits` = JSON_SET(MarketLimits, '$.MesoPurchased', 0)");
            });
        }

        // A reset's rows and its server-info time are stored in one transaction, the time first: a session's save
        // reads the time with a share lock before it writes (GetResetStamps), so it either waits for the whole reset
        // and takes it in memory before writing, or is stored before the reset, which then resets what it wrote. The
        // statements used to be stored one by one after the time, so a save between them and the channels' reset
        // wrote the old values back, and a session that ended then kept them. A reset that fails (a deadlock with a
        // save) is rolled back whole and tried again, three tries in all.
        // A try whose commit was stored but whose answer was lost (the connection cut after the commit) failed for
        // this side only: each try first reads the reset's stored time and ends if it moved past the time stored
        // before the first try (storedBefore, the caller's when it tries again itself: WorldServer.TryReset). A
        // second run would reset again what the players did since the first (the day's clears, revivals, listings).
        private void RunReset(string key, DateTime? storedBefore, Action statements) {
            lock (Context) {
                DateTime? before = storedBefore;
                for (int tries = 1; ; tries++) {
                    try {
                        DateTime stamp = GetResetStamp(key);
                        if (stamp > (before ??= stamp)) {
                            Logger.LogInformation("{Key}: stored at {Stamp} by an earlier try, not run again", key, stamp);
                            return;
                        }
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
