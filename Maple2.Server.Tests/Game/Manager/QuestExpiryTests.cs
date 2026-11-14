using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Manager;

// Quest 7 (Expired): the client sends the quests in progress whose end time has come (CQuestManager::FUN_1416d3010,
// questmanager_2.cpp:2157). The server drops only those that are over by its own clock too, and a quest completed
// before goes back to its last completion as an abandoned one does (QuestManager.Drop).
public class QuestExpiryTests {
    // KR 80020000: a daily (repeatable 2) that ends 1440 minutes after its start.
    private const int Daily = 80020000;
    // KR 94000412: a Georg alliance weekly (repeatable 3) that ends at the next Friday (usePeriod fri), and cannot be
    // abandoned (disableGiveup).
    private const int Weekly = 94000412;

    private TestGameSession test = null!;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.Start();
        test.TakeSent();
    }

    private static long Ago(TimeSpan span) => DateTimeOffset.UtcNow.Add(-span).ToUnixTimeSeconds();

    private Quest Hold(int id, long start) {
        QuestMetadata data = TestGameSession.QuestData(id, id == Daily ? QuestType.EventQuest : QuestType.AllianceQuest);
        data = data with {
            Basic = id == Daily
                ? data.Basic with { Forfeitable = true, Repeat = 2, DurationMinutes = 1440 }
                : data.Basic with { Forfeitable = false, Repeat = 3, ResetWeekday = 6, Alliance = (AllianceType) 9, Rank = 0 },
        };
        test.Register(data);
        Quest quest = test.StartQuest(data);
        quest.StartTime = start;
        return quest;
    }

    private bool Stored(int id) {
        using GameStorage.Request db = test.Storage.Context();
        return db.GetQuests(test.Session.CharacterId).ContainsKey(id);
    }

    // The quest ids of the Quest 7 answers sent.
    private List<int> ExpiredSent() {
        var ids = new List<int>();
        foreach (byte[] packet in test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.Quest && packet[2] == 7)) {
            int count = BitConverter.ToInt32(packet, 3);
            for (int i = 0; i < count; i++) {
                ids.Add(BitConverter.ToInt32(packet, 7 + 4 * i));
            }
        }
        return ids;
    }

    // The client sends a quest once; a client clock up to ExpirySlack seconds ahead still has it dropped, more is not.
    [Test]
    public void Expired_AllowsTheSlackOnly() {
        Hold(Daily, Ago(TimeSpan.FromMinutes(1440) - TimeSpan.FromSeconds(QuestManager.ExpirySlack - 10)));
        test.Session.Quest.Expired([Daily]);
        Assert.That(ExpiredSent(), Is.EqualTo(new[] { Daily }));
        Assert.That(test.Session.Quest.TryGetQuest(Daily, out _), Is.False);

        Quest early = Hold(Daily, Ago(TimeSpan.FromMinutes(1440) - TimeSpan.FromSeconds(QuestManager.ExpirySlack + 30)));
        test.Session.Quest.Expired([Daily]);
        Assert.That(ExpiredSent(), Is.Empty);
        Assert.That(test.Session.Quest.TryGetQuest(Daily, out Quest? held) && ReferenceEquals(held, early), Is.True);
    }

    [Test]
    public void Expired_DropsOnlyQuestsOverByTheServerClock() {
        Hold(Daily, Ago(TimeSpan.FromHours(25)));
        Hold(Weekly, Ago(TimeSpan.FromDays(8)));

        test.Session.Quest.Expired([Daily, Weekly]);

        Assert.That(ExpiredSent(), Is.EqualTo(new[] { Daily, Weekly }));
        Assert.That(test.Session.Quest.TryGetQuest(Daily, out _), Is.False);
        Assert.That(test.Session.Quest.TryGetQuest(Weekly, out _), Is.False);
        Assert.That(Stored(Daily), Is.False);
        Assert.That(Stored(Weekly), Is.False);
    }

    [Test]
    public void Expired_KeepsAQuestNotOverYet() {
        // An hour into the daily's 24 hours, and the weekly started now: it ends at the start of the next Friday,
        // which on a Thursday can be close (skipped within the slack and a minute of it). The weekly cannot be
        // abandoned, and an early expiry was the way around that.
        Quest daily = Hold(Daily, Ago(TimeSpan.FromHours(1)));
        Quest weekly = Hold(Weekly, Ago(TimeSpan.Zero));
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Assume.That(QuestRules.EndTime(weekly.Metadata.Basic, weekly.StartTime, now, null) - now, Is.GreaterThan(QuestManager.ExpirySlack + 60));

        test.Session.Quest.Expired([Daily, Weekly]);

        Assert.That(ExpiredSent(), Is.Empty);
        Assert.That(test.Session.Quest.TryGetQuest(Daily, out Quest? heldDaily) && ReferenceEquals(heldDaily, daily), Is.True);
        Assert.That(test.Session.Quest.TryGetQuest(Weekly, out Quest? heldWeekly) && ReferenceEquals(heldWeekly, weekly), Is.True);
        Assert.That(daily.State, Is.EqualTo(QuestState.Started));
        Assert.That(weekly.State, Is.EqualTo(QuestState.Started));
        Assert.That(Stored(Daily) && Stored(Weekly), Is.True);
    }

    [Test]
    public void Expired_RestoresTheLastCompletionOfARepeatQuest() {
        // The daily was completed two days ago and started again 25 hours ago (Start kept the last times).
        long lastStart = Ago(TimeSpan.FromDays(2));
        long lastEnd = lastStart + 600;
        Quest daily = Hold(Daily, Ago(TimeSpan.FromHours(25)));
        daily.CompletionCount = 3;
        daily.LastStartTime = lastStart;
        daily.LastEndTime = lastEnd;

        test.Session.Quest.Expired([Daily]);

        Assert.That(ExpiredSent(), Is.EqualTo(new[] { Daily }));
        Assert.That(test.Session.Quest.TryGetQuest(Daily, out Quest? held) && ReferenceEquals(held, daily), Is.True);
        Assert.That(Stored(Daily), Is.True);
        Assert.That(daily.State, Is.EqualTo(QuestState.Completed));
        Assert.That(daily.CompletionCount, Is.EqualTo(3));
        Assert.That((daily.StartTime, daily.EndTime, daily.LastStartTime, daily.LastEndTime), Is.EqualTo((lastStart, lastEnd, 0L, 0L)));
        // Completed on another day: it may be started again at once, as after the expiry of a first run.
        Assert.That(QuestManager.CanStartAgain(daily), Is.True);
    }

    [Test]
    public void Expired_IgnoresAQuestWithoutAnEndTime() {
        QuestMetadata data = TestGameSession.QuestData(10003060);
        test.Register(data);
        Quest quest = test.StartQuest(data);
        quest.StartTime = Ago(TimeSpan.FromDays(400));

        test.Session.Quest.Expired([10003060, 1]);

        Assert.That(ExpiredSent(), Is.Empty);
        Assert.That(test.Session.Quest.TryGetQuest(10003060, out _), Is.True);
    }
}
