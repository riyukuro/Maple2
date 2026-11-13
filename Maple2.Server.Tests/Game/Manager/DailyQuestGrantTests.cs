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

// The quests the server grants (DailyQuestGrant): the day's field quest, the week's mission point ladder, the day's daily missions and a married player's wedding missions, at login and at the daily reset, once a
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

    private static readonly int[] LowLadder = [70900001, 70900002, 70900003];
    private static readonly int[] HighLadder = [70900011, 70900012, 70900013];
    private static readonly int[] Ladders = LowLadder.Concat(HighLadder).ToArray();

    private static QuestMetadata MissionPoint(int id, int minLevel, int maxLevel, bool notInPlayedClient = false) {
        QuestMetadata quest = TestGameSession.QuestData(id, QuestType.DailyMission);
        return quest with {
            Basic = quest.Basic with {
                Repeat = 1, GuideType = QuestDailyReset.GuideMissionPoint, GuideMinLevel = minLevel, GuideMaxLevel = maxLevel,
                NotInPlayedClient = notInPlayedClient,
            },
        };
    }

    private void RegisterLadders(Func<int, bool>? notInPlayedClient = null) {
        foreach (int id in LowLadder) {
            test.Register(MissionPoint(id, 1, 49, notInPlayedClient?.Invoke(id) ?? false));
        }
        foreach (int id in HighLadder) {
            test.Register(MissionPoint(id, 50, 99, notInPlayedClient?.Invoke(id) ?? false));
        }
    }

    private static DateTime Week => DailyQuestGrant.MissionPointWeekStart(DateTime.Now);

    [TestCase(50, new[] { 70900011, 70900012, 70900013 })]
    [TestCase(99, new[] { 70900011, 70900012, 70900013 })]
    [TestCase(49, new[] { 70900001, 70900002, 70900003 })]
    [TestCase(1, new[] { 70900001, 70900002, 70900003 })]
    public void MissionPoint_LoginGrantsTheBandsLadder(int level, int[] expected) {
        RegisterLadders();
        Character.Level = (short) level;

        test.Session.Quest.Load();

        Assert.That(Held(Ladders), Is.EqualTo(expected));
        Assert.That(Character.MissionPointGrantTime, Is.EqualTo(Epoch(Week)));
        Assert.That(test.Errors, Is.Empty);
    }

    // A ladder quest the played client lacks is left out; the rest of the ladder is granted.
    [Test]
    public void MissionPoint_NotInPlayedClient() {
        RegisterLadders(id => id == HighLadder[1]);

        test.Session.Quest.Load();

        Assert.That(Held(Ladders), Is.EqualTo(new[] { HighLadder[0], HighLadder[2] }));
    }

    // Reaching level 50 mid-week keeps the old band's ladder until the next Friday.
    [Test]
    public void MissionPoint_MidWeekLevelUpKeepsTheLadder() {
        RegisterLadders();
        Character.Level = 49;
        test.Session.Quest.Load();
        Assert.That(Held(Ladders), Is.EqualTo(LowLadder));

        Character.Level = 50;
        test.Session.Quest.Load();
        test.Session.Quest.ResetDailyQuests(DateTime.Today);

        Assert.That(Held(Ladders), Is.EqualTo(LowLadder));
    }

    // Once a week: a relog after the ladder was dropped (abandoned) grants nothing again that week.
    [Test]
    public void MissionPoint_OncePerWeek() {
        RegisterLadders();
        test.Session.Quest.Load();
        foreach (int id in HighLadder) {
            Assert.That(test.Session.Quest.TryGetQuest(id, out Quest? quest) && test.Session.Quest.Remove(quest));
        }

        test.Session.Quest.Load();

        Assert.That(Held(Ladders), Is.Empty);
    }

    // A reset in a new week grants the ladder; a second reset the same day neither drops it nor grants a second set.
    [Test]
    public void MissionPoint_ResetGrantsOnce() {
        RegisterLadders();
        Character.MissionPointGrantTime = Epoch(Week.AddDays(-7));

        test.Session.Quest.ResetDailyQuests(DateTime.Today);
        Assert.That(Held(Ladders), Is.EqualTo(HighLadder));
        Assert.That(Character.MissionPointGrantTime, Is.EqualTo(Epoch(Week)));

        test.Session.Quest.ResetDailyQuests(DateTime.Today);
        Assert.That(Held(Ladders), Is.EqualTo(HighLadder));
        Assert.That(test.Errors, Is.Empty);
    }

    [Test]
    public void MissionPoint_GrantIsStored() {
        RegisterLadders();
        test.Session.Quest.Load();
        using (GameStorage.Request db = test.Storage.Context()) {
            Assert.That(db.SaveCharacter(Character), Is.True);
        }
        Assert.That(test.StoredCharacter().MissionPointGrantTime, Is.EqualTo(Epoch(Week)));
    }

    // The week starts on Friday: on a Friday it is that day, on a Thursday the Friday before.
    [TestCase(2026, 11, 13, 12, 13)]
    [TestCase(2026, 11, 13, 0, 13)]
    [TestCase(2026, 11, 19, 23, 13)]
    [TestCase(2026, 11, 20, 0, 20)]
    public void MissionPointWeekStart(int year, int month, int day, int hour, int startDay) {
        Assert.That(DailyQuestGrant.MissionPointWeekStart(new DateTime(year, month, day, hour, 0, 0)),
            Is.EqualTo(new DateTime(year, month, startDay)));
    }

    [TestCase(1, 1, 49, true)]
    [TestCase(49, 1, 49, true)]
    [TestCase(50, 1, 49, false)]
    [TestCase(49, 50, 99, false)]
    [TestCase(99, 50, 99, true)]
    [TestCase(120, 50, 0, true)]
    public void InBand(int level, int min, int max, bool inBand) {
        QuestMetadataBasic basic = TestGameSession.QuestData(1, QuestType.DailyMission).Basic with { GuideMinLevel = min, GuideMaxLevel = max };
        Assert.That(DailyQuestGrant.InBand(basic, level), Is.EqualTo(inBand));
    }

    // Four daily missions in each of the six groups: 1-99 in groups 0 to 2, 50-99 in groups 3 to 5 (as in KR, where
    // specup, farmmeso and farmingredient start at 50).
    private static int[] DailyGroup(int group) => Enumerable.Range(0, 4).Select(i => 70100000 + group * 100 + i).ToArray();
    private static readonly int[] DailyMissions = Enumerable.Range(0, 6).SelectMany(DailyGroup).ToArray();

    private static QuestMetadata DailyMission(int id, int group, int minLevel, int maxLevel, int guideType = QuestDailyReset.GuideDailyPickRepeat,
                                              bool notInPlayedClient = false) {
        QuestMetadata quest = TestGameSession.QuestData(id, QuestType.DailyMission);
        return quest with {
            Basic = quest.Basic with {
                Repeat = guideType == QuestDailyReset.GuideDailyPickOnce ? 0 : 1, GuideType = guideType, GuideGroup = group,
                GuideMinLevel = minLevel, GuideMaxLevel = maxLevel, NotInPlayedClient = notInPlayedClient,
            },
        };
    }

    private void RegisterDailyMissions(Func<int, bool>? notInPlayedClient = null) {
        for (int group = 0; group < 6; group++) {
            foreach (int id in DailyGroup(group)) {
                test.Register(DailyMission(id, group, group < 3 ? 1 : 50, 99,
                    id % 2 == 0 ? QuestDailyReset.GuideDailyPickOnce : QuestDailyReset.GuideDailyPickRepeat, notInPlayedClient?.Invoke(id) ?? false));
            }
        }
    }

    // The group's first quest in the character's order of the day, among those not left out.
    private int DailyPick(int group, Func<int, bool>? leftOut = null) {
        IEnumerable<QuestMetadata> candidates = DailyGroup(group).Where(id => leftOut?.Invoke(id) != true)
            .Select(id => test.Session.QuestMetadata.TryGet(id, out QuestMetadata? metadata) ? metadata! : throw new InvalidOperationException());
        return DailyQuestGrant.DailyMissionOrder(candidates, test.Session.CharacterId, DateTime.Today, group).First().Id;
    }

    [Test]
    public void DailyMission_LoginGrantsOnePerGroup() {
        RegisterDailyMissions();

        test.Session.Quest.Load();

        Assert.That(Held(DailyMissions), Is.EqualTo(Enumerable.Range(0, 6).Select(group => DailyPick(group)).Order().ToArray()));
        Assert.That(Character.DailyMissionGrantTime, Is.EqualTo(Epoch(DateTime.Today)));
        Assert.That(test.Errors, Is.Empty);
    }

    // Below 50 only the groups with a band that holds the level get one: three a day.
    [Test]
    public void DailyMission_BelowFiftyThreeGroups() {
        RegisterDailyMissions();
        Character.Level = 49;

        test.Session.Quest.Load();

        Assert.That(Held(DailyMissions), Is.EqualTo(Enumerable.Range(0, 3).Select(group => DailyPick(group)).Order().ToArray()));
    }

    // A pick the played client lacks is skipped: the group's next quest in the day's order is granted instead.
    [Test]
    public void DailyMission_NotInPlayedClientNextInOrder() {
        RegisterDailyMissions();
        int skipped = DailyPick(1);
        test = RegisterAgain(id => id == skipped);

        test.Session.Quest.Load();

        int[] held = Held(DailyGroup(1));
        Assert.That(held, Is.EqualTo(new[] { DailyPick(1, id => id == skipped) }));
    }

    private TestGameSession RegisterAgain(Func<int, bool> notInPlayedClient) {
        SetUp();
        RegisterDailyMissions(notInPlayedClient);
        return test;
    }

    // A relog, or a relog after the day's missions were dropped (abandoned), grants nothing again that day; a level
    // up the same day adds no group.
    [Test]
    public void DailyMission_OncePerDay() {
        RegisterDailyMissions();
        Character.Level = 49;
        test.Session.Quest.Load();
        int[] first = Held(DailyMissions);
        Assert.That(first, Has.Length.EqualTo(3));

        Character.Level = 50;
        test.Session.Quest.Load();
        Assert.That(Held(DailyMissions), Is.EqualTo(first));

        foreach (int id in first) {
            Assert.That(test.Session.Quest.TryGetQuest(id, out Quest? quest) && test.Session.Quest.Remove(quest));
        }
        test.Session.Quest.Load();
        Assert.That(Held(DailyMissions), Is.Empty);
    }

    // A group that already holds a daily mission (from before the grant was kept) is left as it is.
    [Test]
    public void DailyMission_HeldGroupLeftAsIs() {
        RegisterDailyMissions();
        int other = DailyGroup(2).First(id => id != DailyPick(2));
        test.Session.QuestMetadata.TryGet(other, out QuestMetadata? metadata);
        Quest quest = test.StartQuest(metadata!);
        quest.StartTime = Epoch(DateTime.Now);

        test.Session.Quest.Load();

        Assert.That(DailyGroup(2).Where(id => test.Session.Quest.TryGetQuest(id, out _)), Is.EqualTo(new[] { other }));
        Assert.That(Held(DailyGroup(0)), Is.EqualTo(new[] { DailyPick(0) }));
    }

    // The reset of a new day grants the day's set after its drop; a second reset the same day drops it (as the client
    // does on 0x19) and grants no second set.
    [Test]
    public void DailyMission_ResetGrantsOnce() {
        RegisterDailyMissions();
        Character.DailyMissionGrantTime = Epoch(DateTime.Today.AddDays(-1));

        test.Session.Quest.ResetDailyQuests(DateTime.Today);
        Assert.That(Held(DailyMissions), Has.Length.EqualTo(6));
        Assert.That(Character.DailyMissionGrantTime, Is.EqualTo(Epoch(DateTime.Today)));

        test.Session.Quest.ResetDailyQuests(DateTime.Today);
        Assert.That(Held(DailyMissions), Is.Empty);
        Assert.That(test.Errors, Is.Empty);
    }

    [Test]
    public void DailyMission_GrantIsStored() {
        RegisterDailyMissions();
        test.Session.Quest.Load();
        using (GameStorage.Request db = test.Storage.Context()) {
            Assert.That(db.SaveCharacter(Character), Is.True);
        }
        Assert.That(test.StoredCharacter().DailyMissionGrantTime, Is.EqualTo(Epoch(DateTime.Today)));
    }

    // The order is the same for the same character, day and group, and another for another character or day.
    [Test]
    public void DailyMissionOrder_PerCharacterPerDay() {
        QuestMetadata[] quests = Enumerable.Range(0, 40).Select(i => DailyMission(70100000 + i, 1, 1, 99)).ToArray();
        int[] Order(long characterId, DateTime day) => DailyQuestGrant.DailyMissionOrder(quests.Reverse(), characterId, day, 1).Select(q => q.Id).ToArray();
        DateTime today = new(2026, 11, 13);

        Assert.That(Order(1, today), Is.EqualTo(Order(1, today)));
        Assert.That(Order(1, today), Is.EquivalentTo(quests.Select(q => q.Id)));
        Assert.That(Order(2, today), Is.Not.EqualTo(Order(1, today)));
        Assert.That(Order(1, today.AddDays(1)), Is.Not.EqualTo(Order(1, today)));
    }

    private static readonly int[] WeddingMissions = Enumerable.Range(0, 8).Select(i => 72000001 + i).ToArray();

    private void RegisterWeddingMissions(Func<int, bool>? notInPlayedClient = null) {
        foreach (int id in WeddingMissions) {
            QuestMetadata quest = TestGameSession.QuestData(id, QuestType.WeddingMission);
            test.Register(quest with {
                Basic = quest.Basic with {
                    Repeat = 1, GuideType = QuestDailyReset.GuideDailyPickRepeat, GuideMinLevel = 1, GuideMaxLevel = 99,
                    NotInPlayedClient = notInPlayedClient?.Invoke(id) ?? false,
                },
            });
        }
    }

    private void Marry(MaritalStatus status = MaritalStatus.Married) {
        test.Session.Marriage.Marriage = new Marriage {
            Id = 1, Status = status, Partner1 = new MarriagePartner(), Partner2 = new MarriagePartner(),
        };
    }

    // The first count wedding missions in the character's order of the day, among those not left out.
    private int[] WeddingPicks(int count = DailyQuestGrant.WeddingMissionsPerDay, Func<int, bool>? leftOut = null) {
        IEnumerable<QuestMetadata> candidates = WeddingMissions.Where(id => leftOut?.Invoke(id) != true)
            .Select(id => test.Session.QuestMetadata.TryGet(id, out QuestMetadata? metadata) ? metadata! : throw new InvalidOperationException());
        return DailyQuestGrant.DailyMissionOrder(candidates, test.Session.CharacterId, DateTime.Today, DailyQuestGrant.WeddingMissionSeedIndex)
            .Take(count).Select(metadata => metadata.Id).Order().ToArray();
    }

    [TestCase(MaritalStatus.Married)]
    [TestCase(MaritalStatus.DivorceFiledByGroom)]
    [TestCase(MaritalStatus.DivorceFiledByBride)]
    public void WeddingMission_MarriedGetsThree(MaritalStatus status) {
        RegisterWeddingMissions();
        Marry(status);

        test.Session.Quest.Load();

        Assert.That(Held(WeddingMissions), Is.EqualTo(WeddingPicks()));
        Assert.That(Held(WeddingMissions), Has.Length.EqualTo(3));
        Assert.That(Character.WeddingMissionGrantTime, Is.EqualTo(Epoch(DateTime.Today)));
        Assert.That(test.Errors, Is.Empty);
    }

    // Single or engaged: none, and the day stays to be granted (a marriage the same day gets them at the next login).
    [TestCase(MaritalStatus.Single)]
    [TestCase(MaritalStatus.Engaged)]
    public void WeddingMission_NotMarriedNone(MaritalStatus status) {
        RegisterWeddingMissions();
        Marry(status);

        test.Session.Quest.Load();

        Assert.That(Held(WeddingMissions), Is.Empty);
        Assert.That(Character.WeddingMissionGrantTime, Is.Zero);

        Marry();
        test.Session.Quest.Load();
        Assert.That(Held(WeddingMissions), Is.EqualTo(WeddingPicks()));
    }

    // A mission the played client lacks is skipped: the next one in the day's order takes its place.
    [Test]
    public void WeddingMission_NotInPlayedClientNextInOrder() {
        RegisterWeddingMissions();
        int skipped = WeddingPicks()[0];
        SetUp();
        RegisterWeddingMissions(id => id == skipped);
        Marry();

        test.Session.Quest.Load();

        Assert.That(Held(WeddingMissions), Is.EqualTo(WeddingPicks(leftOut: id => id == skipped)));
    }

    // A relog, or a relog after one was dropped (abandoned), grants no other set that day; a wedding mission already
    // held counts as granted.
    [Test]
    public void WeddingMission_OncePerDay() {
        RegisterWeddingMissions();
        Marry();
        test.Session.Quest.Load();
        int[] first = Held(WeddingMissions);

        test.Session.Quest.Load();
        Assert.That(Held(WeddingMissions), Is.EqualTo(first));

        Assert.That(test.Session.Quest.TryGetQuest(first[0], out Quest? quest) && test.Session.Quest.Remove(quest));
        test.Session.Quest.Load();
        Assert.That(Held(WeddingMissions), Is.EqualTo(first.Skip(1).ToArray()));
    }

    [Test]
    public void WeddingMission_HeldCountsAsGranted() {
        RegisterWeddingMissions();
        Marry();
        test.Session.QuestMetadata.TryGet(WeddingMissions.First(id => !WeddingPicks().Contains(id)), out QuestMetadata? metadata);
        test.StartQuest(metadata!).StartTime = Epoch(DateTime.Now);

        test.Session.Quest.Load();

        Assert.That(WeddingPicks().Where(id => test.Session.Quest.TryGetQuest(id, out _)), Is.Empty);
    }

    // The reset of a new day grants the day's three after the 0x26 drop; a second reset drops them and grants none.
    [Test]
    public void WeddingMission_ResetGrantsOnce() {
        RegisterWeddingMissions();
        Marry();
        Character.WeddingMissionGrantTime = Epoch(DateTime.Today.AddDays(-1));

        test.Session.Quest.ResetDailyQuests(DateTime.Today);
        Assert.That(Held(WeddingMissions), Is.EqualTo(WeddingPicks()));

        test.Session.Quest.ResetDailyQuests(DateTime.Today);
        Assert.That(Held(WeddingMissions), Is.Empty);
        Assert.That(test.Errors, Is.Empty);
    }

    [Test]
    public void WeddingMission_GrantIsStored() {
        RegisterWeddingMissions();
        Marry();
        test.Session.Quest.Load();
        using (GameStorage.Request db = test.Storage.Context()) {
            Assert.That(db.SaveCharacter(Character), Is.True);
        }
        Assert.That(test.StoredCharacter().WeddingMissionGrantTime, Is.EqualTo(Epoch(DateTime.Today)));
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
