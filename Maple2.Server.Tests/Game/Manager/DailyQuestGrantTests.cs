using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Manager;

// The quests the server grants (DailyQuestGrant): the day's field quest, at login and at the daily reset, once a
// period, only what the player can start.
public class DailyQuestGrantTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int FieldGroup = DailyQuestGrant.FieldQuestGroup;
    private static readonly int[] FieldQuests = [91000001, 91000002, 91000003, 91000004, 91000005];

    private TestGameSession test = null!;
    private readonly Dictionary<int, int[]> groups = new();

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.SetTable(new FameTable([], new Dictionary<int, AllianceType>()));
        test.Start();
        // The in-memory metadata has no quest table: the first read finds none and marks the cache as the table, so
        // the quests registered after it are the whole table; the group index is set as GetGroup builds it.
        Assume.That(test.Session.QuestMetadata.GetQuests(), Is.Empty);
        typeof(QuestMetadataStorage).GetField("groups", Any)!.SetValue(test.Session.QuestMetadata, groups);
        test.Session.Player.Value.Character.Level = 50;
        test.TakeSent();
    }

    private static QuestMetadata FieldQuest(int id, bool notInPlayedClient = false) {
        QuestMetadata quest = TestGameSession.QuestData(id, QuestType.FieldQuest);
        return quest with {
            Basic = quest.Basic with { Repeat = 2, NotInPlayedClient = notInPlayedClient },
            Require = quest.Require with { Level = 50, GroupId = FieldGroup },
        };
    }

    private void RegisterFieldQuests(Func<int, bool>? notInPlayedClient = null) {
        foreach (int id in FieldQuests) {
            test.Register(FieldQuest(id, notInPlayedClient?.Invoke(id) ?? false));
        }
        groups[FieldGroup] = FieldQuests;
    }

    private static int FieldPick() => QuestGroupPick.Pick(FieldQuests, 2, FieldGroup, DateTime.Now).Single();

    private static long Epoch(DateTime local) => new DateTimeOffset(local).ToUnixTimeSeconds();

    // The held quests among ids, checked against the stored rows.
    private int[] Held(IEnumerable<int> ids) {
        using GameStorage.Request db = test.Storage.Context();
        int[] stored = db.GetQuests(test.Session.CharacterId).Keys.Concat(db.GetQuests(test.Session.AccountId).Keys).Distinct().Order().ToArray();
        int[] held = ids.Where(id => test.Session.Quest.TryGetQuest(id, out _)).Order().ToArray();
        Assert.That(held, Is.EqualTo(stored.Where(ids.Contains).ToArray()));
        return held;
    }

    private Character Character => test.Session.Player.Value.Character;

    [Test]
    public void FieldQuest_LoginGrantsTheDaysPick() {
        RegisterFieldQuests();

        test.Session.Quest.Load();

        Assert.That(Held(FieldQuests), Is.EqualTo(new[] { FieldPick() }));
        Assert.That(test.Session.Quest.TryGetQuest(FieldPick(), out Quest? quest) && quest.State == QuestState.Started);
        Assert.That(Character.FieldQuestGrantTime, Is.EqualTo(Epoch(DateTime.Today)));
        Assert.That(test.Errors, Is.Empty);
    }

    // Below the require level the pick cannot be started: nothing, and the day stays to be granted (a later login
    // at level 50 the same day gets it).
    [Test]
    public void FieldQuest_BelowLevelNothing() {
        RegisterFieldQuests();
        Character.Level = 49;

        test.Session.Quest.Load();

        Assert.That(Held(FieldQuests), Is.Empty);
        Assert.That(Character.FieldQuestGrantTime, Is.Zero);

        Character.Level = 50;
        test.Session.Quest.Load();
        Assert.That(Held(FieldQuests), Is.EqualTo(new[] { FieldPick() }));
    }

    // A quest the played client lacks is never granted, and no other quest takes the pick's place.
    [Test]
    public void FieldQuest_PickNotInPlayedClient() {
        int pick = FieldPick();
        RegisterFieldQuests(id => id == pick);

        test.Session.Quest.Load();

        Assert.That(Held(FieldQuests), Is.Empty);
    }

    // A relog, or a relog after the quest was dropped (abandoned), the same day grants nothing again.
    [Test]
    public void FieldQuest_OncePerDay() {
        RegisterFieldQuests();
        test.Session.Quest.Load();
        Assert.That(test.Session.Quest.TryGetQuest(FieldPick(), out Quest? quest));

        test.Session.Quest.Load();
        Assert.That(Held(FieldQuests), Is.EqualTo(new[] { FieldPick() }));

        Assert.That(test.Session.Quest.Remove(quest!));
        test.Session.Quest.Load();
        Assert.That(Held(FieldQuests), Is.Empty);
    }

    // A field quest of the day already held (from before the grant was kept) counts as the day's grant.
    [Test]
    public void FieldQuest_HeldCountsAsGranted() {
        RegisterFieldQuests();
        int other = FieldQuests.First(id => id != FieldPick());
        test.Session.QuestMetadata.TryGet(other, out QuestMetadata? metadata);
        Quest quest = test.StartQuest(metadata!);
        quest.State = QuestState.Completed;
        quest.StartTime = Epoch(DateTime.Now);

        test.Session.Quest.Load();

        Assert.That(test.Session.Quest.TryGetQuest(FieldPick(), out _), Is.False);
    }

    // The reset grants the new day's pick after its drop; a second reset the same day drops it (as the client does on
    // the packet) and grants nothing again.
    [Test]
    public void FieldQuest_ResetGrantsOnce() {
        RegisterFieldQuests();
        Character.FieldQuestGrantTime = Epoch(DateTime.Today.AddDays(-1));

        test.Session.Quest.ResetDailyQuests(DateTime.Today);

        Assert.That(Held(FieldQuests), Is.EqualTo(new[] { FieldPick() }));
        Assert.That(Character.FieldQuestGrantTime, Is.EqualTo(Epoch(DateTime.Today)));

        test.Session.Quest.ResetDailyQuests(DateTime.Today);
        Assert.That(Held(FieldQuests), Is.Empty);
        Assert.That(test.Errors, Is.Empty);
    }

    // The grant is kept with the character.
    [Test]
    public void FieldQuest_GrantIsStored() {
        RegisterFieldQuests();
        test.Session.Quest.Load();
        using (GameStorage.Request db = test.Storage.Context()) {
            Assert.That(db.SaveCharacter(Character), Is.True);
        }
        Assert.That(test.StoredCharacter().FieldQuestGrantTime, Is.EqualTo(Epoch(DateTime.Today)));
    }

    [TestCase(0L, true)]
    [TestCase(-1L, true)]
    [TestCase(1L, false)]
    public void Due(long offset, bool due) {
        DateTime day = new(2026, 11, 13);
        long dayStart = Epoch(day);
        Assert.That(DailyQuestGrant.Due(offset == 0 ? 0 : dayStart + offset - 1, day), Is.EqualTo(due));
    }
}
