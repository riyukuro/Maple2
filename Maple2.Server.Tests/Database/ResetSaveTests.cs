using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Maple2.Server.Tests.Game;
using Maple2.Server.World.Service;
using Maple2.Tools.Scheduler;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Maple2.Server.Tests.Database;

// The World's daily reset sets the stored rows with raw SQL; an online session takes it in memory once, from the
// World's message or from its next save, which reads the reset's time before it writes. The save used to write the
// state from before the reset back over the reset rows (a death count or the day's prestige levels kept for good once
// the session ended). The reset is MySQL SQL: the tests need MS2_TEST_GAME_DB, a scratch MySQL game database (the
// reset runs over all of its rows).
public class ResetSaveTests {
    private static readonly string? Connection = Environment.GetEnvironmentVariable("MS2_TEST_GAME_DB");

    private DbContextOptions options = null!;
    private TestGameSession test = null!;
    private GameSession session => test.Session;

    [SetUp]
    public void SetUp() {
        if (Connection == null) {
            Assert.Ignore("the reset is MySQL SQL: set MS2_TEST_GAME_DB");
        }
        options = new DbContextOptionsBuilder().UseMySql(Connection, ServerVersion.AutoDetect(Connection)).Options;
        test = new TestGameSession(options);
        test.Start();
        // The managers a full save writes, as SessionSaveTransactionTests sets them, and the queue the reset uses.
        test.SetTable(new JobTable(new Dictionary<JobCode, JobTable.Entry> {
            [JobCode.Newbie] = new(new JobTable.Tutorial(0, 0, 0, [], [], [], []), new Dictionary<SkillRank, JobTable.Skill[]> {
                [SkillRank.Basic] = [], [SkillRank.Awakening] = [],
            }, []),
        }));
        test.SetTable(new PrestigeLevelRewardTable(new Dictionary<int, PrestigeLevelRewardMetadata>()));
        using (GameStorage.Request db = test.Storage.Context()) {
            session.Config = new ConfigManager(db, session);
        }
        session.Survival = new SurvivalManager(session);
        typeof(GameSession).GetField(nameof(GameSession.Scheduler))!.SetValue(session, new EventQueue(Serilog.Log.Logger));
        // The first save: the session holds the resets stored up to now.
        Assume.That(session.SessionSave(), Is.True);
        Assume.That(test.Errors, Is.Empty);
    }

    private (int Levels, int Revivals) Stored() {
        using var context = new Ms2Context(options);
        int levels = context.Account.AsNoTracking().Single(account => account.Id == test.AccountId).PrestigeLevelsGained;
        int revivals = context.CharacterConfig.AsNoTracking().Single(config => config.CharacterId == test.CharacterId).InstantRevivalCount;
        return (levels, revivals);
    }

    private void Played() {
        session.Player.Value.Account.PrestigeLevelsGained = 3;
        session.Config.InstantReviveCount = 2;
    }

    private void WorldDailyReset() {
        using GameStorage.Request db = test.Storage.Context();
        db.DailyReset();
    }

    // A save after the reset, before the World's message: the session takes the reset and stores it.
    [Test]
    public void SaveAfterTheReset_StoresTheReset() {
        Played();
        Assume.That(session.SessionSave(), Is.True);
        Assume.That(Stored(), Is.EqualTo((3, 2)));

        WorldDailyReset();
        Assert.That(session.SessionSave(), Is.True);
        Assert.Multiple(() => {
            Assert.That(Stored(), Is.EqualTo((0, 0)));
            Assert.That((session.Player.Value.Account.PrestigeLevelsGained, session.Config.InstantReviveCount), Is.EqualTo((0, 0)));
        });
    }

    // The World's message after a save took the reset does not reset the session again: what it did since stays.
    [Test]
    public void MessageAfterTheSave_DoesNotResetAgain() {
        WorldDailyReset();
        Assume.That(session.SessionSave(), Is.True);
        Played();

        GameStorage.ResetStamps stamps;
        using (GameStorage.Request db = test.Storage.Context()) {
            stamps = db.GetResetStamps();
        }
        session.TakeResets(stamps);
        Assert.That((session.Player.Value.Account.PrestigeLevelsGained, session.Config.InstantReviveCount), Is.EqualTo((3, 2)));
        Assert.That(session.SessionSave(), Is.True);
        Assert.That(Stored(), Is.EqualTo((3, 2)));
    }

    private bool Ended => (int) typeof(Maple2.Server.Core.Network.Session).GetField("disconnecting", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(session)! == 1;

    // A reset being stored, as RunReset holds it: its server-info row is written first and held until the commit
    // (Dispose without Commit rolls it back). Also holds the rows of this account a daily reset writes.
    private sealed class ResetHold : IDisposable {
        private readonly Ms2Context context;
        private readonly IDbContextTransaction transaction;

        public ResetHold(DbContextOptions options, string key, long accountId, long characterId) {
            context = new Ms2Context(options);
            transaction = context.Database.BeginTransaction();
            context.Database.ExecuteSqlRaw("INSERT INTO `server-info` (`Key`, `LastModified`) VALUES ({0}, {1}) ON DUPLICATE KEY UPDATE `LastModified` = VALUES(`LastModified`)",
                key, DateTime.Now);
            if (key == "DailyReset") {
                context.Database.ExecuteSqlRaw("UPDATE `account` SET `PrestigeLevelsGained` = DEFAULT WHERE `Id` = {0}", accountId);
                context.Database.ExecuteSqlRaw("UPDATE `character-config` SET `InstantRevivalCount` = 0 WHERE `CharacterId` = {0}", characterId);
            }
        }

        public void Commit() => transaction.Commit();

        public void Dispose() {
            transaction.Dispose();
            context.Dispose();
        }
    }

    // A timed save that finds a reset being stored does not wait for it on the field thread and is no failed save: it
    // comes back 2 s later, as often as needed, and stores once the reset is done. It used to wait for the lock in the
    // database (up to 50 s, the field thread held), fail, and its try again ended the session: a reset cut players.
    [Test]
    public void TimedSave_ResetRunning_ComesBackLater() {
        test.EnterMap(TestGameSession.Map(2000023));
        typeof(GameSession).GetField("preMigrationSaved", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(session, false);
        session.State = SessionState.Connected;
        Played();
        int queued = session.Field!.Scheduler.Count;

        var watch = Stopwatch.StartNew();
        using (new ResetHold(options, "DailyReset", test.AccountId, test.CharacterId)) {
            session.TimedSaveOnField(session.Field!);
            Assert.That((Ended, test.Errors.Count, session.Field!.Scheduler.Count), Is.EqualTo((false, 0, queued + 1)));
            session.TimedSaveRetry(session.Field!);
            Assert.That((Ended, test.Errors.Count, session.Field!.Scheduler.Count), Is.EqualTo((false, 0, queued + 2)));
        }
        Assert.That(watch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(5)), "the field thread waited for the reset");
        Assert.That(Stored(), Is.EqualTo((0, 0)));

        // The reset rolled back: the next timed save stores.
        session.TimedSaveOnField(session.Field!);
        Assert.That((Ended, Stored()), Is.EqualTo((false, (3, 2))));
    }

    // A session save (logout, field entry) waits for a reset being stored, then takes it and stores it.
    [Test]
    public void SessionSave_ResetRunning_WaitsAndTakesIt() {
        Played();
        Assume.That(session.SessionSave(), Is.True);
        Assume.That(Stored(), Is.EqualTo((3, 2)));

        var hold = new ResetHold(options, "DailyReset", test.AccountId, test.CharacterId);
        var release = new Thread(() => {
            Thread.Sleep(1000);
            hold.Commit();
            hold.Dispose();
        });
        var watch = Stopwatch.StartNew();
        release.Start();
        bool saved = session.SessionSave();
        release.Join();
        Assert.Multiple(() => {
            Assert.That((saved, test.Errors.Count), Is.EqualTo((true, 0)));
            Assert.That(watch.Elapsed, Is.GreaterThan(TimeSpan.FromMilliseconds(900)));
            Assert.That(Stored(), Is.EqualTo((0, 0)));
            Assert.That((session.Player.Value.Account.PrestigeLevelsGained, session.Config.InstantReviveCount), Is.EqualTo((0, 0)));
        });
    }

    // The monthly reset, as the daily one: a save after it takes it and stores it.
    [Test]
    public void SaveAfterTheMonthlyReset_StoresTheReset() {
        session.Player.Value.Account.MesoMarketPurchased = 5;
        Assume.That(session.SessionSave(), Is.True);
        Assume.That(StoredMesoPurchased(), Is.EqualTo(5));

        using (GameStorage.Request db = test.Storage.Context()) {
            db.MonthlyReset();
        }
        Assert.That(session.SessionSave(), Is.True);
        Assert.That((StoredMesoPurchased(), session.Player.Value.Account.MesoMarketPurchased), Is.EqualTo((0, 0)));
    }

    private int StoredMesoPurchased() {
        using var context = new Ms2Context(options);
        return context.Account.AsNoTracking().Single(account => account.Id == test.AccountId).MarketLimits.MesoPurchased;
    }

    // A purchase's save after the reset takes it as the session save does.
    [Test]
    public void PurchaseAfterTheReset_TakesTheReset() {
        Played();
        Assume.That(session.SessionSave(), Is.True);

        WorldDailyReset();
        Assert.That(session.SavePurchase(), Is.True);
        Assert.That((session.Player.Value.Account.PrestigeLevelsGained, session.Config.InstantReviveCount), Is.EqualTo((0, 0)));
        Assert.That(session.SessionSave(), Is.True);
        Assert.That(Stored(), Is.EqualTo((0, 0)));
    }

    // A daily and a monthly reset taken together while one step of the daily reset throws (here the shop's, its
    // manager taken away; live, a send or the season mail's World call): the daily reset's other steps and the monthly
    // reset still run, and the next save stores them. The throw used to skip the rest, which counted as taken, so the
    // save wrote the month's meso market purchases back over the stored reset.
    [Test]
    public void TakeResets_StepFails_OthersStillTaken() {
        Played();
        session.Player.Value.Account.MesoMarketPurchased = 5;
        Assume.That(session.SessionSave(), Is.True);
        Assume.That((Stored(), StoredMesoPurchased()), Is.EqualTo(((3, 2), 5)));

        GameStorage.ResetStamps stamps;
        using (GameStorage.Request db = test.Storage.Context()) {
            db.DailyReset();
            db.MonthlyReset();
            stamps = db.GetResetStamps();
        }
        test.Errors.Clear();
        ShopManager shop = session.Shop;
        session.Shop = null!;
        Assert.DoesNotThrow(() => session.TakeResets(stamps));
        session.Shop = shop;
        Assert.That(test.Errors, Is.Not.Empty, "the failed step is logged");

        test.Errors.Clear();
        Assert.That(session.SessionSave(), Is.True);
        Assert.Multiple(() => {
            Assert.That(session.Player.Value.Account.MesoMarketPurchased, Is.EqualTo(0));
            Assert.That(StoredMesoPurchased(), Is.EqualTo(0));
            Assert.That(Stored(), Is.EqualTo((0, 0)));
        });
    }

    // A login whose load finds a reset being stored past its wait is a failed load as the others: the move error
    // (0x0D: MoveResult) is sent and the session closed. The ResetRunningException used to escape the handler.
    [Test]
    public void Login_ResetRunning_FailedLoad() {
        // The server's session tables EnterServer adds the session to.
        object server = typeof(GameSession).GetField("server", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(session)!;
        Type gameServer = typeof(Maple2.Server.Game.GameServer);
        gameServer.GetField("mutex", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(server, new object());
        gameServer.GetField("connectingSessions", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(server, new HashSet<GameSession>());
        gameServer.GetField("sessions", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(server, new System.Collections.Concurrent.ConcurrentDictionary<long, GameSession>());
        test.World.MigrateInAnswer = new MigrateInResponse { CharacterId = test.CharacterId, Channel = 1, MapId = 2000023 };
        test.TakeSent();

        TimeSpan wait = GameSession.ResetWait;
        GameSession.ResetWait = TimeSpan.FromMilliseconds(500);
        try {
            using (new ResetHold(options, "DailyReset", test.AccountId, test.CharacterId)) {
                Assert.DoesNotThrow(() => new ResponseKeyHandler { World = test.World }.Handle(session, TestGameSession.Packet(writer => {
                    writer.WriteLong(test.AccountId);
                    writer.WriteLong(77);
                    writer.WriteBytes(Guid.NewGuid().ToByteArray());
                })));
            }
        } finally {
            GameSession.ResetWait = wait;
        }
        bool ended = Ended; // read before TakeSent, which opens the session again
        Assert.Multiple(() => {
            Assert.That(test.TakeSentOps(), Is.EqualTo(new[] { SendOp.MoveResult }));
            Assert.That(ended, Is.True);
            Assert.That(test.World.Released, Is.EqualTo(test.World.Acquired), "the account lock is let go");
        });
    }
}
