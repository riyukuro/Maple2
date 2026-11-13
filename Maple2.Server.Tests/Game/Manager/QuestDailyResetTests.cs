using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Manager;

// The daily quest resets (Quest 0x19 daily missions, 0x1E daily field quests, 0x26 wedding missions): the rows the
// client drops on them are deleted at the reset, at login after a missed reset, and for a player online across it.
public class QuestDailyResetTests {
    private const int LevelGuide = 1;
    private const int PickOnce = 3;
    private const int PickRepeat = 4;
    private const int MissionPoint = 5;

    private TestGameSession test = null!;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        // What a quest load reads: no alliance missions.
        test.SetTable(new FameTable([], new Dictionary<int, AllianceType>()));
        test.Start();
        test.TakeSent();
    }

    private static QuestMetadataBasic Basic(QuestType type, int guide = 0, int repeat = 0) =>
        TestGameSession.QuestData(1, type).Basic with { GuideType = guide, Repeat = repeat };

    private static long Epoch(DateTime local) => new DateTimeOffset(local).ToUnixTimeSeconds();

    // A held quest with its stored row, started at start (local), in state; an account quest (account > 0) is held
    // and stored under the account.
    private Quest Hold(int id, QuestType type, DateTime start, int guide = 0, int repeat = 0, QuestState state = QuestState.Started,
                       int account = 0) {
        QuestMetadata data = TestGameSession.QuestData(id, type);
        data = data with { Basic = data.Basic with { GuideType = guide, Repeat = repeat, Account = account } };
        test.Register(data);
        Quest quest = test.StartQuest(data);
        quest.State = state;
        quest.StartTime = Epoch(start);
        using GameStorage.Request db = test.Storage.Context();
        Assume.That(db.CreateQuest(account > 0 ? test.Session.AccountId : test.Session.CharacterId, quest), Is.Not.Null);
        return quest;
    }

    // The session's own queue (the test session has none); the delayed reset is queued on it.
    private Maple2.Tools.Scheduler.EventQueue Scheduler() {
        var scheduler = new Maple2.Tools.Scheduler.EventQueue(Serilog.Log.Logger);
        scheduler.Start();
        typeof(GameSession).GetField(nameof(GameSession.Scheduler))!.SetValue(test.Session, scheduler);
        return scheduler;
    }

    private int[] Held() {
        using GameStorage.Request db = test.Storage.Context();
        int[] stored = db.GetQuests(test.Session.CharacterId).Keys.Concat(db.GetQuests(test.Session.AccountId).Keys).Distinct().Order().ToArray();
        int[] held = new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }.Where(id => test.Session.Quest.TryGetQuest(id, out _)).ToArray();
        Assert.That(held, Is.EqualTo(stored));
        return held;
    }

    // The Quest packets of the reset: mode 0x19 with the day's midnight, then 0x1E and 0x26 with the mode only.
    private void AssertResetPackets(DateTime day) {
        byte[][] quest = test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.Quest).ToArray();
        Assert.That(quest, Has.Length.EqualTo(3));
        Assert.That(quest[0], Has.Length.EqualTo(11));
        Assert.That(quest[0][2], Is.EqualTo(0x19));
        Assert.That(BitConverter.ToInt64(quest[0], 3), Is.EqualTo(Epoch(day)));
        Assert.That(quest[1], Is.EqualTo(new byte[] { quest[1][0], quest[1][1], 0x1E }));
        Assert.That(quest[2], Is.EqualTo(new byte[] { quest[2][0], quest[2][1], 0x26 }));
    }

    [TestCase(QuestType.DailyMission, PickOnce, 0, QuestDailyReset.Kind.DailyMission)]
    [TestCase(QuestType.DailyMission, PickRepeat, 1, QuestDailyReset.Kind.DailyMission)]
    [TestCase(QuestType.DailyMission, MissionPoint, 0, QuestDailyReset.Kind.DailyMission)]
    [TestCase(QuestType.DailyMission, LevelGuide, 0, QuestDailyReset.Kind.None)]
    [TestCase(QuestType.DailyMission, 2, 0, QuestDailyReset.Kind.None)]
    [TestCase(QuestType.DailyMission, 0, 0, QuestDailyReset.Kind.None)]
    [TestCase(QuestType.FieldQuest, 0, 2, QuestDailyReset.Kind.FieldQuest)]
    [TestCase(QuestType.FieldQuest, 0, 0, QuestDailyReset.Kind.None)]
    [TestCase(QuestType.FieldQuest, 0, 3, QuestDailyReset.Kind.None)]
    [TestCase(QuestType.WeddingMission, 0, 1, QuestDailyReset.Kind.WeddingMission)]
    [TestCase(QuestType.EpicQuest, PickRepeat, 2, QuestDailyReset.Kind.None)]
    public void KindOf(QuestType type, int guide, int repeat, QuestDailyReset.Kind kind) {
        Assert.That(QuestDailyReset.KindOf(Basic(type, guide, repeat)), Is.EqualTo(kind));
    }

    // Mission point missions end at the start of the Friday after their start (2026-11-09 is a Monday).
    [TestCase("2026-11-12 23:59:59", false)]
    [TestCase("2026-11-13 00:00:00", true)]
    [TestCase("2026-11-16 00:00:00", true)]
    public void MissionPoint_EndsOnFriday(string now, bool dropped) {
        long start = Epoch(new DateTime(2026, 11, 9, 10, 0, 0));
        QuestMetadataBasic basic = Basic(QuestType.DailyMission, MissionPoint);
        Assert.That(QuestDailyReset.Dropped(basic, start, DateTime.Parse(now), true), Is.EqualTo(dropped));
        Assert.That(QuestDailyReset.Dropped(basic, start, DateTime.Parse(now), false), Is.EqualTo(dropped));
    }

    // At login a daily row goes when started before today; at the reset always.
    [TestCase(-1, false, true)]
    [TestCase(0, false, false)]
    [TestCase(0, true, true)]
    public void DailyRow_Window(int days, bool atReset, bool dropped) {
        DateTime today = new(2026, 11, 13);
        long start = Epoch(today.AddDays(days).AddHours(23));
        foreach (QuestMetadataBasic basic in new[] {
                     Basic(QuestType.DailyMission, PickRepeat, 1), Basic(QuestType.FieldQuest, 0, 2), Basic(QuestType.WeddingMission),
                 }) {
            Assert.That(QuestDailyReset.Dropped(basic, start, today.AddHours(1), atReset), Is.EqualTo(dropped));
        }
        Assert.That(QuestDailyReset.Dropped(Basic(QuestType.DailyMission, LevelGuide), start, today.AddHours(1), atReset), Is.False);
    }

    // The reset at midnight drops every daily row in any state (the client drops them on the packets, rewarded or
    // not) and keeps the level guides, the weekly field quests and the epic quests (mission points: MissionPoint_EndsOnFriday).
    [Test]
    public void Reset_AtMidnight() {
        DateTime today = DateTime.Today;
        DateTime started = today.AddHours(-2);
        Hold(1, QuestType.DailyMission, started, PickOnce, state: QuestState.Completed);
        Hold(2, QuestType.DailyMission, started, PickRepeat, 1);
        Hold(3, QuestType.FieldQuest, started, repeat: 2);
        Hold(4, QuestType.WeddingMission, started, repeat: 1, state: QuestState.Completed);
        Hold(5, QuestType.DailyMission, started, LevelGuide);
        Hold(6, QuestType.FieldQuest, started, repeat: 3);
        Hold(7, QuestType.EpicQuest, started);

        test.Session.Quest.ResetDailyQuests(today);

        Assert.That(Held(), Is.EqualTo(new[] { 5, 6, 7 }));
        AssertResetPackets(today);
        Assert.That(test.Errors, Is.Empty);
    }

    // A timer that fires before midnight waits for it: the daily completed earlier that day is kept (dropped at
    // once, it could be taken and rewarded again the same day), nothing is sent, and the reset runs at midnight with
    // its midnight in the packet.
    [Test]
    public void Reset_EarlyFireWaitsForMidnight() {
        Maple2.Tools.Scheduler.EventQueue scheduler = Scheduler();
        DateTime today = DateTime.Today;
        Hold(1, QuestType.DailyMission, today.AddHours(-3), PickOnce, state: QuestState.Completed);
        Hold(2, QuestType.DailyMission, today.AddHours(-3), LevelGuide);

        test.Session.Quest.ResetDailyQuests(today.AddHours(-1));

        Assert.That(Held(), Is.EqualTo(new[] { 1, 2 }));
        Assert.That(test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.Quest), Is.Empty);
        // One timed event (an hour away), none to run now.
        Assert.That(scheduler.Count, Is.EqualTo(1));
        Assert.That(scheduler.Queued, Is.Zero);
    }

    // The queued reset runs with its midnight: the reset itself, nothing queued again.
    [Test]
    public void Reset_QueuedRunsAtMidnight() {
        Maple2.Tools.Scheduler.EventQueue scheduler = Scheduler();
        DateTime today = DateTime.Today;
        Hold(1, QuestType.DailyMission, today.AddHours(-3), PickOnce, state: QuestState.Completed);
        Hold(2, QuestType.DailyMission, today.AddHours(-3), LevelGuide);

        test.Session.Quest.ResetDailyQuests(today);

        Assert.That(scheduler.Count, Is.Zero);
        Assert.That(Held(), Is.EqualTo(new[] { 2 }));
        AssertResetPackets(today);
    }

    // Account quests (held and stored under the account) go at the reset like the character's.
    [Test]
    public void Reset_AccountRows() {
        DateTime today = DateTime.Today;
        DateTime started = today.AddHours(-2);
        Hold(1, QuestType.DailyMission, started, PickOnce, state: QuestState.Completed, account: 1);
        Hold(2, QuestType.WeddingMission, started, repeat: 1, account: 2);
        Hold(3, QuestType.FieldQuest, started, repeat: 2, account: 1);
        Hold(4, QuestType.DailyMission, started, LevelGuide, account: 1);
        Hold(5, QuestType.DailyMission, started, PickRepeat, 1);

        test.Session.Quest.ResetDailyQuests(today);

        Assert.That(Held(), Is.EqualTo(new[] { 4 }));
        AssertResetPackets(today);
        Assert.That(test.Errors, Is.Empty);
    }

    // At login the account's daily rows started before today go, the ones started today stay.
    [Test]
    public void Login_AccountRows() {
        DateTime today = DateTime.Today;
        Hold(1, QuestType.DailyMission, today.AddHours(-1), PickOnce, state: QuestState.Completed, account: 1);
        Hold(2, QuestType.WeddingMission, today.AddHours(-1), repeat: 1, account: 1);
        Hold(3, QuestType.DailyMission, DateTime.Now, PickRepeat, 1, account: 1);
        Hold(4, QuestType.DailyMission, today.AddHours(-1), LevelGuide, account: 1);

        test.Session.Quest.Load();

        Assert.That(Held(), Is.EqualTo(new[] { 3, 4 }));
    }

    // Mission point missions through the reset: at the Friday reset the ones started before that Friday go, the one
    // started on it stays (2026-11-13 is a Friday).
    [Test]
    public void MissionPoint_Reset() {
        DateTime friday = new(2026, 11, 13);
        Hold(1, QuestType.DailyMission, friday.AddHours(-14), MissionPoint, state: QuestState.Completed);
        Hold(2, QuestType.DailyMission, friday.AddDays(-4), MissionPoint);
        Hold(3, QuestType.DailyMission, friday, MissionPoint);
        Hold(4, QuestType.DailyMission, friday.AddHours(-1), PickOnce);

        test.Session.Quest.ResetDailyQuests(friday);

        Assert.That(Held(), Is.EqualTo(new[] { 3 }));
        AssertResetPackets(friday);
        Assert.That(test.Errors, Is.Empty);
    }

    // Through the reset on a Thursday the mission point missions of that week stay.
    [Test]
    public void MissionPoint_ResetBeforeFriday() {
        DateTime thursday = new(2026, 11, 12);
        Hold(1, QuestType.DailyMission, thursday.AddDays(-3), MissionPoint, state: QuestState.Completed);
        Hold(2, QuestType.DailyMission, thursday.AddHours(-1), PickOnce);

        test.Session.Quest.ResetDailyQuests(thursday);

        Assert.That(Held(), Is.EqualTo(new[] { 1 }));
        AssertResetPackets(thursday);
    }

    // At login a mission point mission goes once a Friday has come since its start (8 days ago, always), and stays
    // when started now.
    [Test]
    public void MissionPoint_Login() {
        Hold(1, QuestType.DailyMission, DateTime.Now.AddDays(-8), MissionPoint, state: QuestState.Completed);
        Hold(2, QuestType.DailyMission, DateTime.Now, MissionPoint);

        test.Session.Quest.Load();

        Assert.That(Held(), Is.EqualTo(new[] { 2 }));
        Assert.That(test.Errors, Is.Empty);
    }

    // Logged out across the reset: the login drops the rows started on an earlier day, keeps the ones started today
    // and sends no reset packet (the client gets only the rows the server sends).
    [Test]
    public void Login_AfterMissedReset() {
        DateTime today = DateTime.Today;
        DateTime yesterday = today.AddHours(-1);
        Hold(1, QuestType.DailyMission, yesterday, PickOnce, state: QuestState.Completed);
        Hold(2, QuestType.FieldQuest, yesterday, repeat: 2);
        Hold(3, QuestType.WeddingMission, yesterday, repeat: 1);
        Hold(4, QuestType.DailyMission, yesterday, LevelGuide);
        Hold(5, QuestType.DailyMission, DateTime.Now, PickRepeat, 1);
        Hold(6, QuestType.FieldQuest, DateTime.Now, repeat: 2);

        test.Session.Quest.Load();

        Assert.That(Held(), Is.EqualTo(new[] { 4, 5, 6 }));
        Assert.That(test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.Quest)
            .Select(packet => packet[2]), Has.None.EqualTo(0x19).And.None.EqualTo(0x1E).And.None.EqualTo(0x26));
    }

    // Online across midnight: the rows started before it go at the reset; the quests taken after it stay at the
    // next login.
    [Test]
    public void Online_AcrossMidnight() {
        DateTime today = DateTime.Today;
        Hold(1, QuestType.DailyMission, today.AddMinutes(-30), PickRepeat, 1);
        Hold(2, QuestType.WeddingMission, today.AddMinutes(-30), repeat: 1);
        Hold(3, QuestType.DailyMission, today.AddMinutes(-30), LevelGuide);

        test.Session.Quest.ResetDailyQuests(today);
        Assert.That(Held(), Is.EqualTo(new[] { 3 }));
        AssertResetPackets(today);

        Hold(4, QuestType.DailyMission, DateTime.Now, PickRepeat, 1);
        Hold(5, QuestType.FieldQuest, DateTime.Now, repeat: 2);
        test.Session.Quest.Load();
        Assert.That(Held(), Is.EqualTo(new[] { 3, 4, 5 }));
        Assert.That(test.Errors, Is.Empty);
    }
}
