using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
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

    // A purchase whose caller gives back what it took (TrySavePurchase) is refused at once while a reset is being
    // stored: the field thread does not wait for the reset, and nothing is logged as a failed save. It used to wait
    // up to 60 s on the field thread.
    [Test]
    public void TrySavePurchase_ResetRunning_RefusedAtOnce() {
        test.EnterMap(TestGameSession.Map(2000023));
        test.Errors.Clear();
        int saved = 0;
        var watch = Stopwatch.StartNew();
        bool stored;
        using (new ResetHold(options, "DailyReset", test.AccountId, test.CharacterId)) {
            stored = session.TrySavePurchase(_ => ++saved > 0);
        }
        Assert.Multiple(() => {
            Assert.That(watch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(5)), "the field thread waited for the reset");
            Assert.That((stored, saved, test.Errors.Count), Is.EqualTo((false, 0, 0)));
        });
        Assert.That(session.TrySavePurchase(_ => ++saved > 0), Is.True, "the reset done, the next purchase stores");
    }

    // A purchase whose caller goes on whatever it answers (SavePurchase) comes back 2 s later on the field's queue
    // while a reset is being stored, as the timed save does, and stores then.
    [Test]
    public void SavePurchase_ResetRunning_ComesBackLater() {
        test.EnterMap(TestGameSession.Map(2000023));
        typeof(GameSession).GetField("preMigrationSaved", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(session, false);
        session.State = SessionState.Connected;
        test.Errors.Clear();
        int queued = session.Field!.Scheduler.Count;
        int saved = 0;
        var watch = Stopwatch.StartNew();
        using (new ResetHold(options, "DailyReset", test.AccountId, test.CharacterId)) {
            Assert.That(session.SavePurchase(_ => saved++), Is.False);
        }
        Assert.Multiple(() => {
            Assert.That(watch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(5)), "the field thread waited for the reset");
            Assert.That((saved, test.Errors.Count, session.Field!.Scheduler.Count), Is.EqualTo((0, 0, queued + 1)));
        });

        // The reset rolled back: the queued try again stores the purchase.
        var timed = (System.Collections.IList) typeof(EventQueue).GetField("timedEvents", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(session.Field!.Scheduler)!;
        object retry = timed.Cast<object>().Last();
        var task = (Action) retry.GetType().GetField("task", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(retry)!;
        timed.Remove(retry);
        task();
        Assert.That((saved, test.Errors.Count, session.Field!.Scheduler.Count), Is.EqualTo((1, 0, queued)));
    }

    // A field mission whose last counter is filled while a reset is being stored: its completion is refused (its save
    // does not wait on the field thread) and comes back 2 s later on the field's queue, or at the next field entry
    // when the session left the field; it completes once then, and never again. It used to be refused for good.
    [TestCase(false)]
    [TestCase(true)]
    public void FieldMission_ResetRunning_CompletedOnceLater(bool leftTheField) {
        test.EnterMap(TestGameSession.Map(2000023));
        typeof(GameSession).GetField("preMigrationSaved", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(session, false);
        session.State = SessionState.Connected;
        // The tables a completion reads, as StampEventRulesTests sets them.
        test.SetTable(new ChangeJobTable(new Dictionary<Job, ChangeJobMetadata>()));
        test.SetTable(new ChapterBookTable(new Dictionary<int, ChapterBookTable.Entry>()));
        test.SetTable(new FameTable([], new Dictionary<int, AllianceType>()));
        test.Errors.Clear();
        Quest quest = test.StartQuest(TestGameSession.QuestData(990259, QuestType.FieldMission) with {
            Conditions = [new ConditionMetadata(ConditionType.trigger, 1, new ConditionMetadata.Parameters(Strings: ["rv259"]), null)],
        });
        FieldManager field = session.Field!;
        int queued = field.Scheduler.Count;
        using (new ResetHold(options, "DailyReset", test.AccountId, test.CharacterId)) {
            session.Quest.Update(ConditionType.trigger, codeString: "rv259");
            // A second refusal while the try again waits queues no other one.
            session.Quest.CompleteFieldMissions();
        }
        Assert.Multiple(() => {
            Assert.That((quest.Conditions[0].Counter, quest.State, quest.CompletionCount), Is.EqualTo((1, QuestState.Started, 0)));
            Assert.That((test.Errors.Count, field.Scheduler.Count), Is.EqualTo((0, queued + 1)));
        });

        if (leftTheField) {
            // The try again finds the session on another field and does nothing; the field entry completes it.
            test.EnterMap(TestGameSession.Map(2000024));
            RunQueued(field);
            Assert.That(quest.State, Is.EqualTo(QuestState.Started));
            session.Quest.CompleteFieldMissions();
        } else {
            RunQueued(field);
        }
        Assert.That((quest.State, quest.CompletionCount, test.Errors.Count, field.Scheduler.Count),
            Is.EqualTo((QuestState.Completed, 1, 0, queued)));

        // Never twice: the next field entry and a later update leave it completed once.
        session.Quest.CompleteFieldMissions();
        session.Quest.Update(ConditionType.trigger, codeString: "rv259");
        Assert.That((quest.State, quest.CompletionCount), Is.EqualTo((QuestState.Completed, 1)));
        using var context = new Ms2Context(options);
        Maple2.Database.Model.Quest stored = context.Quest.Find(test.CharacterId, 990259)!;
        Assert.That((stored.State, stored.CompletionCount), Is.EqualTo((QuestState.Completed, 1)), "the stored row");
    }

    // A stamp card's daily mission whose last counter is filled while the daily reset is being stored, and which that
    // reset then drops (as the session takes it): the try again 2 s later completes nothing, pays nothing and writes
    // no row back (the reset deleted it), so the day's grant is not blocked by a completed row. When the day's grant
    // started it again (a new quest, its counter at 0), the try again leaves that one alone too; it used to store the
    // dropped one over its row as completed and pay its reward.
    [TestCase(false)]
    [TestCase(true)]
    public void StampDailyMission_ResetDropsIt_TryAgainDoesNothing(bool startedAgain) {
        test.EnterMap(TestGameSession.Map(2000023));
        typeof(GameSession).GetField("preMigrationSaved", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(session, false);
        session.State = SessionState.Connected;
        test.SetTable(new ChangeJobTable(new Dictionary<Job, ChangeJobMetadata>()));
        test.SetTable(new ChapterBookTable(new Dictionary<int, ChapterBookTable.Entry>()));
        test.SetTable(new FameTable([], new Dictionary<int, AllianceType>()));
        test.Errors.Clear();
        QuestMetadata metadata = TestGameSession.QuestData(990260, QuestType.DailyMission) with {
            Conditions = [new ConditionMetadata(ConditionType.trigger, 1, new ConditionMetadata.Parameters(Strings: ["rv259"]), null)],
        };
        Quest quest = test.StartQuest(metadata with { Basic = metadata.Basic with { GuideType = QuestDailyReset.GuideDailyPickOnce } });
        typeof(QuestManager).GetField("stampQuests", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(session.Quest, new HashSet<int> { 990260 });
        FieldManager field = session.Field!;
        int queued = field.Scheduler.Count;
        using (new ResetHold(options, "DailyReset", test.AccountId, test.CharacterId)) {
            session.Quest.Update(ConditionType.trigger, codeString: "rv259");
        }
        Assume.That((quest.State, field.Scheduler.Count), Is.EqualTo((QuestState.Started, queued + 1)), "refused, a try again queued");

        // The reset's drop of the day's missions, as the session takes it.
        typeof(QuestManager).GetMethod("DropDailyQuests", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(session.Quest, [DateTime.Now, true]);
        Assume.That(session.Quest.TryGetQuest(990260, out _), Is.False);
        Quest? again = startedAgain ? test.StartQuest(quest.Metadata) : null;
        RunQueued(field);

        using var context = new Ms2Context(options);
        Maple2.Database.Model.Quest? stored = context.Quest.Find(test.CharacterId, 990260);
        Assert.Multiple(() => {
            Assert.That((quest.State, quest.CompletionCount, test.Errors.Count, field.Scheduler.Count), Is.EqualTo((QuestState.Started, 0, 0, queued)));
            if (again == null) {
                Assert.That(stored, Is.Null, "no row written back");
            } else {
                Assert.That((again.State, again.Conditions[0].Counter), Is.EqualTo((QuestState.Started, 0)));
                Assert.That((stored?.State, stored?.CompletionCount), Is.EqualTo(((QuestState?) QuestState.Started, (int?) 0)), "the day's row");
            }
        });
    }

    // Runs the last try again queued on the field now (it waits 2 s).
    private static void RunQueued(FieldManager field) {
        var timed = (System.Collections.IList) typeof(EventQueue).GetField("timedEvents", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(field.Scheduler)!;
        object retry = timed.Cast<object>().Last();
        var task = (Action) retry.GetType().GetField("task", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(retry)!;
        timed.Remove(retry);
        task();
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
