using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using Maple2.Database.Extensions;
using Maple2.File.Ingest.Mapper;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

// The alliance mission lists (Quest 0x1F and 0x20): table/famepickmethod.xml and famedef.xml read as the client's
// CFameTable does, and the server's pick from them. The xml here is made up, in the files' shape.
public class AllianceMissionsTests {
    private const string PickMethodXml = """
        <ms2>
            <environment locale="">
                <method type="RankAllianceCombination" repeatType="2" pickCountByRank="2,1" reloadOnAccept="" reloadOnComplete="100" feature="On">
                    <alliance id="DarkWind" maxCount="1" />
                    <alliance id="mapleunion" maxCount="1" />
                </method>
                <method type="FixedCountPerAlliance" repeatType="3" pickCountByRank="1,0" reloadOnComplete="100" feature="On">
                    <alliance id="DarkWind" />
                </method>
                <method type="FixedCountPerAlliance" repeatType="2" pickCountByRank="1" feature="Off">
                    <alliance id="GreenHood" />
                </method>
            </environment>
            <environment locale="NA">
                <method type="FixedCountPerAlliance" repeatType="2" pickCountByRank="1,1" reloadOnComplete="200">
                    <alliance id="Lumiknight_KritiasExped" />
                    <alliance id="Unknown" />
                </method>
            </environment>
        </ms2>
        """;

    private const string FameDefXml = """
        <ms2>
            <season id="1">
                <fame alliance="DarkWind" officerNpcKind="100" />
                <fame alliance="GreenHood_KritiasExped" officerNpcKind="107" />
                <fame alliance="TriaRoyalGuard" />
            </season>
        </ms2>
        """;

    private static FameTable Read(string locale) {
        var pickMethod = new XmlDocument();
        pickMethod.LoadXml(PickMethodXml);
        var fameDef = new XmlDocument();
        fameDef.LoadXml(FameDefXml);
        return TableMapper.ReadFame(pickMethod, fameDef, locale, feature => feature != "Off");
    }

    // KR has no environment of its own: the one without a locale, without the methods whose feature is off.
    [Test]
    public void ReadFame_EnvironmentWithoutLocale() {
        FameTable table = Read("KR");

        Assert.That(table.PickMethods, Has.Count.EqualTo(2));
        FameTable.PickMethod daily = table.PickMethods[0];
        Assert.Multiple(() => {
            Assert.That(daily.Combination, Is.True);
            Assert.That(daily.Repeat, Is.EqualTo(2));
            Assert.That(daily.PickCountByRank, Is.EqualTo(new[] { 2, 1 }));
            Assert.That(daily.ReloadOnAccept, Is.EqualTo(0));
            Assert.That(daily.ReloadOnComplete, Is.EqualTo(100));
            Assert.That(daily.Alliances, Is.EqualTo(new[] {
                new FameTable.Alliance(AllianceType.DarkWind, 1), new FameTable.Alliance(AllianceType.MapleAlliance, 1),
            }));
            Assert.That(table.PickMethods[1].Combination, Is.False);
            Assert.That(table.PickMethods[1].Repeat, Is.EqualTo(3));
            Assert.That(table.OfficerNpcKinds, Is.EqualTo(new Dictionary<int, AllianceType> {
                [100] = AllianceType.DarkWind,
                [107] = AllianceType.KritiasGreenHoods,
            }));
        });
    }

    // A locale with its own environment takes it, and an unknown alliance name is left out.
    [Test]
    public void ReadFame_EnvironmentOfTheLocale() {
        FameTable table = Read("na");

        Assert.That(table.PickMethods, Has.Count.EqualTo(1));
        Assert.That(table.PickMethods[0].ReloadOnComplete, Is.EqualTo(200));
        Assert.That(table.PickMethods[0].Alliances.Select(alliance => alliance.Type),
            Is.EqualTo(new[] { AllianceType.KritiasLumiknights }));
    }

    private static readonly FameTable.PickMethod Combination = new(true, 2, [2, 1], 0, 0, [
        new FameTable.Alliance(AllianceType.DarkWind, 1), new FameTable.Alliance(AllianceType.MapleAlliance, 0),
    ]);

    private static readonly FameTable.PickMethod Fixed = new(false, 3, [1, 2], 0, 0, [
        new FameTable.Alliance(AllianceType.DarkWind, 0), new FameTable.Alliance(AllianceType.MapleAlliance, 0),
    ]);

    private static List<AllianceMissions.Candidate> Candidates() {
        var candidates = new List<AllianceMissions.Candidate>();
        int id = 1;
        foreach (AllianceType alliance in new[] { AllianceType.DarkWind, AllianceType.MapleAlliance, AllianceType.GreenHoods }) {
            for (int rank = 0; rank < 3; rank++) {
                for (int i = 0; i < 3; i++) {
                    candidates.Add(new AllianceMissions.Candidate(id++, alliance, rank));
                }
            }
        }
        return candidates;
    }

    // Combination: each rank's count from the listed alliances together, DarkWind at most 1 over all ranks.
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(77)]
    public void Pick_CombinationKeepsCountsAndCaps(int seed) {
        List<AllianceMissions.Candidate> candidates = Candidates();
        List<AllianceMissions.Candidate> picks = AllianceMissions.Pick(Combination, candidates, seed)
            .Select(id => candidates.Single(candidate => candidate.Id == id)).ToList();

        Assert.Multiple(() => {
            Assert.That(picks.Count(pick => pick.Rank == 0), Is.EqualTo(2));
            Assert.That(picks.Count(pick => pick.Rank == 1), Is.EqualTo(1));
            Assert.That(picks.Count(pick => pick.Alliance == AllianceType.DarkWind), Is.LessThanOrEqualTo(1));
            Assert.That(picks.Any(pick => pick.Alliance == AllianceType.GreenHoods), Is.False);
            Assert.That(picks.Select(pick => pick.Id).Distinct().Count(), Is.EqualTo(picks.Count));
        });
    }

    // Fixed: every listed alliance gets each rank's count; fewer candidates than a count give them all.
    [Test]
    public void Pick_FixedCountPerAlliance() {
        List<AllianceMissions.Candidate> candidates = Candidates();
        candidates.RemoveAll(candidate => candidate.Alliance == AllianceType.MapleAlliance && candidate.Rank == 1 && candidate.Id % 3 != 0);
        List<AllianceMissions.Candidate> picks = AllianceMissions.Pick(Fixed, candidates, 5)
            .Select(id => candidates.Single(candidate => candidate.Id == id)).ToList();

        Assert.Multiple(() => {
            Assert.That(picks.Count(pick => pick.Alliance == AllianceType.DarkWind && pick.Rank == 0), Is.EqualTo(1));
            Assert.That(picks.Count(pick => pick.Alliance == AllianceType.DarkWind && pick.Rank == 1), Is.EqualTo(2));
            Assert.That(picks.Count(pick => pick.Alliance == AllianceType.MapleAlliance && pick.Rank == 0), Is.EqualTo(1));
            Assert.That(picks.Count(pick => pick.Alliance == AllianceType.MapleAlliance && pick.Rank == 1), Is.EqualTo(1));
            Assert.That(picks.Any(pick => pick.Rank == 2 || pick.Alliance == AllianceType.GreenHoods), Is.False);
        });
    }

    // The same character and period give the same list, in any candidate order; another day gives its own seed.
    [Test]
    public void Pick_SameSeedSameList() {
        List<AllianceMissions.Candidate> candidates = Candidates();
        int seed = AllianceMissions.Seed(12345, new DateTime(2026, 11, 11, 13, 0, 0), 0);
        List<int> first = AllianceMissions.Pick(Combination, candidates, seed);
        candidates.Reverse();

        Assert.Multiple(() => {
            Assert.That(AllianceMissions.Pick(Combination, candidates, seed), Is.EqualTo(first));
            Assert.That(AllianceMissions.Seed(12345, new DateTime(2026, 11, 11, 23, 0, 0), 0), Is.EqualTo(seed));
            Assert.That(AllianceMissions.Seed(12345, new DateTime(2026, 11, 12), 0), Is.Not.EqualTo(seed));
            Assert.That(AllianceMissions.Seed(12345, new DateTime(2026, 11, 11), 1), Is.Not.EqualTo(seed));
        });
    }

    // Daily: the day. Weekly with reset day friday (6): the last friday at or before now (2026-11-11 is a wednesday).
    [TestCase(2, 6, "2026-11-11 15:00", "2026-11-11")]
    [TestCase(3, 6, "2026-11-11 15:00", "2026-11-06")]
    [TestCase(3, 6, "2026-11-13 00:00", "2026-11-13")]
    [TestCase(3, 6, "2026-11-12 23:59", "2026-11-06")]
    [TestCase(3, 0, "2026-11-11 15:00", "2026-11-08")] // none: sunday
    public void PeriodStart(int repeat, int weekday, string now, string expected) {
        Assert.That(AllianceMissions.PeriodStart(repeat, weekday, DateTime.Parse(now)), Is.EqualTo(DateTime.Parse(expected)));
    }

    private static QuestMetadata Quest(QuestType type, int repeat, int groupId, int id = 1,
                                       AllianceType alliance = AllianceType.AnyFaction, int resetWeekday = 0,
                                       int durationMinutes = 0) {
        var basic = new QuestMetadataBasic(ChapterId: 0, Type: type, Account: 0, StandardLevel: 1, Forfeitable: true,
            EventTag: string.Empty, AutoStart: false, Disabled: false, UsePostbox: false, StartNpc: 0, CompleteNpc: 0,
            CompleteMaps: null, ProgressMaps: null, Repeat: repeat, ResetWeekday: resetWeekday,
            DurationMinutes: durationMinutes, Alliance: alliance);
        var require = new QuestMetadataRequire(0, 0, [], [], [], 0, (0, 0), 0, groupId);
        return new QuestMetadata(id, null, basic, require, null!, null!, null!, null!, null!, null!,
            null, null, null, QuestEventMissionType.none, []);
    }

    [TestCase(QuestType.AllianceQuest, 2, 0, true)]
    [TestCase(QuestType.AllianceQuest, 2, 7, true)] // a group only matters to the weekly list
    [TestCase(QuestType.AllianceQuest, 3, 0, true)]
    [TestCase(QuestType.AllianceQuest, 3, 7, false)] // weekly with a group (FameChallengeDungeon)
    [TestCase(QuestType.AllianceQuest, 0, 0, false)]
    [TestCase(QuestType.EpicQuest, 2, 0, false)]
    public void IsMission(QuestType type, int repeat, int groupId, bool expected) {
        Assert.That(AllianceMissions.IsMission(Quest(type, repeat, groupId)), Is.EqualTo(expected));
    }

    private static long Epoch(string time) => DateTime.Parse(time).ToEpochSeconds();

    // M1: the missions started in the period are picked first and use up the counts, so when the missions the player
    // can start change within the period (level, gear score, quests) the list does not grow past the counts, and
    // the ones not started yet keep their places.
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(77)]
    public void Pick_HeldMissionsUseTheCounts(int seed) {
        List<AllianceMissions.Candidate> candidates = Candidates();
        List<int> first = AllianceMissions.Pick(Combination, candidates, seed);
        int held = first[0];

        // The first pick is started; a level up then makes the others of its rank ineligible and new ones eligible.
        AllianceMissions.Candidate heldCandidate = candidates.Single(candidate => candidate.Id == held);
        List<AllianceMissions.Candidate> after = candidates.Select(candidate => candidate.Id == held
            ? candidate with { Held = true, Eligible = false }
            : candidate with { Eligible = candidate.Rank != heldCandidate.Rank || !first.Contains(candidate.Id) })
            .ToList();
        List<AllianceMissions.Candidate> picks = AllianceMissions.Pick(Combination, after, seed)
            .Select(id => after.Single(candidate => candidate.Id == id)).ToList();

        Assert.Multiple(() => {
            Assert.That(picks.Select(pick => pick.Id), Does.Contain(held));
            Assert.That(picks.Count(pick => pick.Rank == 0), Is.EqualTo(2));
            Assert.That(picks.Count(pick => pick.Rank == 1), Is.EqualTo(1));
            Assert.That(picks.Count(pick => pick.Alliance == AllianceType.DarkWind), Is.LessThanOrEqualTo(1));
        });
    }

    // The order is a shuffle of all the missions: a mission the player cannot start only gives up its own place.
    [Test]
    public void Pick_OrderDoesNotDependOnEligibility() {
        List<AllianceMissions.Candidate> candidates = Candidates();
        List<int> first = AllianceMissions.Pick(Fixed, candidates, 9);
        List<AllianceMissions.Candidate> after = candidates
            .Select(candidate => first.Contains(candidate.Id) ? candidate : candidate with { Eligible = false })
            .ToList();

        Assert.That(AllianceMissions.Pick(Fixed, after, 9), Is.EqualTo(first));
    }

    // Fixed: a held mission counts for its alliance and rank, and stays even when over the count.
    [Test]
    public void Pick_FixedHeldOverTheCount() {
        List<AllianceMissions.Candidate> candidates = Candidates()
            .Select(candidate => candidate.Alliance == AllianceType.DarkWind && candidate.Rank == 0 ? candidate with { Held = true } : candidate)
            .ToList();
        List<AllianceMissions.Candidate> picks = AllianceMissions.Pick(Fixed, candidates, 3)
            .Select(id => candidates.Single(candidate => candidate.Id == id)).ToList();

        Assert.Multiple(() => {
            Assert.That(picks.Count(pick => pick.Alliance == AllianceType.DarkWind && pick.Rank == 0), Is.EqualTo(3));
            Assert.That(picks.Count(pick => pick.Alliance == AllianceType.DarkWind && pick.Rank == 1), Is.EqualTo(2));
            Assert.That(picks.Count(pick => pick.Alliance == AllianceType.MapleAlliance && pick.Rank == 0), Is.EqualTo(1));
        });
    }

    // Start without bypass: a mission only from the list of its repeat; other quests are not limited.
    [Test]
    public void Listed_RefusesAMissionNotInTheList() {
        var daily = new HashSet<int> { 10 };
        var weekly = new HashSet<int> { 20 };

        Assert.Multiple(() => {
            Assert.That(AllianceMissions.Listed(Quest(QuestType.AllianceQuest, 2, 0, id: 10), daily, weekly), Is.True);
            Assert.That(AllianceMissions.Listed(Quest(QuestType.AllianceQuest, 2, 0, id: 11), daily, weekly), Is.False);
            Assert.That(AllianceMissions.Listed(Quest(QuestType.AllianceQuest, 2, 0, id: 20), daily, weekly), Is.False);
            Assert.That(AllianceMissions.Listed(Quest(QuestType.AllianceQuest, 3, 0, id: 20), daily, weekly), Is.True);
            Assert.That(AllianceMissions.Listed(Quest(QuestType.AllianceQuest, 3, 7, id: 21), daily, weekly), Is.True);
            Assert.That(AllianceMissions.Listed(Quest(QuestType.EpicQuest, 2, 0, id: 12), daily, weekly), Is.True);
        });
    }

    // The reset and the login drop the missions without a group, not completed, started before the period.
    [TestCase(QuestType.AllianceQuest, 2, 0, QuestState.Started, "2026-11-10 20:00", true)]
    [TestCase(QuestType.AllianceQuest, 2, 0, QuestState.Completed, "2026-11-10 20:00", false)]
    [TestCase(QuestType.AllianceQuest, 2, 0, QuestState.Started, "2026-11-11 00:10", false)]
    [TestCase(QuestType.AllianceQuest, 2, 7, QuestState.Started, "2026-11-10 20:00", false)] // m1: a group stays
    [TestCase(QuestType.AllianceQuest, 3, 0, QuestState.Started, "2026-11-10 20:00", true)]
    [TestCase(QuestType.EpicQuest, 2, 0, QuestState.Started, "2026-11-10 20:00", false)]
    public void Dropped(QuestType type, int repeat, int groupId, QuestState state, string started, bool expected) {
        Assert.That(AllianceMissions.Dropped(Quest(type, repeat, groupId), state, Epoch(started), DateTime.Parse("2026-11-11")),
            Is.EqualTo(expected));
    }

    // m3: a reset that comes a moment (or an hour, after a clock change) before midnight starts the next period.
    [TestCase(2, 0, "2026-11-10 23:00", "2026-11-11")]
    [TestCase(2, 0, "2026-11-11 00:00", "2026-11-11")]
    [TestCase(2, 0, "2026-11-11 01:00", "2026-11-11")]
    [TestCase(3, 6, "2026-11-12 23:59", "2026-11-13")]
    [TestCase(3, 6, "2026-11-11 23:59", "2026-11-06")]
    public void ResetPeriodStart(int repeat, int weekday, string now, string expected) {
        Assert.That(AllianceMissions.ResetPeriodStart(repeat, weekday, DateTime.Parse(now)), Is.EqualTo(DateTime.Parse(expected)));
    }

    // Accept all (Quest 34): the listed missions of the alliance, by id.
    [Test]
    public void AcceptAll_ListedOfTheAlliance() {
        QuestMetadata[] listed = [
            Quest(QuestType.AllianceQuest, 3, 0, id: 30, alliance: AllianceType.DarkWind),
            Quest(QuestType.AllianceQuest, 2, 0, id: 12, alliance: AllianceType.MapleAlliance),
            Quest(QuestType.AllianceQuest, 2, 0, id: 11, alliance: AllianceType.DarkWind),
        ];

        Assert.That(AllianceMissions.AcceptAll(listed, AllianceType.DarkWind), Is.EqualTo(new[] { 11, 30 }));
    }

    // Complete all (Quest 35): held alliance quests of the alliance and of repeat 2 or 3 not completed.
    [TestCase(QuestType.AllianceQuest, 2, AllianceType.DarkWind, QuestState.Started, true)]
    [TestCase(QuestType.AllianceQuest, 3, AllianceType.DarkWind, QuestState.Started, true)]
    [TestCase(QuestType.AllianceQuest, 3, AllianceType.DarkWind, QuestState.Completed, false)]
    [TestCase(QuestType.AllianceQuest, 0, AllianceType.DarkWind, QuestState.Started, false)]
    [TestCase(QuestType.AllianceQuest, 2, AllianceType.MapleAlliance, QuestState.Started, false)]
    [TestCase(QuestType.EpicQuest, 2, AllianceType.DarkWind, QuestState.Started, false)]
    public void CompleteAll(QuestType type, int repeat, AllianceType alliance, QuestState state, bool expected) {
        QuestMetadataBasic basic = Quest(type, repeat, 0, alliance: alliance).Basic;
        Assert.That(AllianceMissions.CompleteAll(basic, state, AllianceType.DarkWind), Is.EqualTo(expected));
    }

    // M2: a completed daily mission stays on Quest 7 and on Abandon, so it cannot be started again the same day.
    [Test]
    public void CompletedDaily_ExpiredThenStartAgainIsRefused() {
        QuestMetadataBasic basic = Quest(QuestType.AllianceQuest, 2, 0).Basic;
        long started = Epoch("2026-11-11 10:00");
        DateTime now = DateTime.Parse("2026-11-11 12:00");

        Assert.Multiple(() => {
            Assert.That(QuestRules.CanExpire(basic, QuestState.Completed), Is.False);
            Assert.That(QuestRules.CanForfeit(basic, QuestState.Completed), Is.False);
            Assert.That(QuestRules.CanStartAgain(basic.Repeat, basic.ResetWeekday, started, now), Is.False);
            Assert.That(AllianceMissions.Dropped(Quest(QuestType.AllianceQuest, 2, 0), QuestState.Completed, started,
                AllianceMissions.PeriodStart(2, 0, now)), Is.False);
        });
    }

    // Quest 7 only for a quest in progress with an end time: a reset weekday, a duration or an event tag.
    [TestCase(0, 0, QuestState.Started, false)]
    [TestCase(6, 0, QuestState.Started, true)]
    [TestCase(0, 30, QuestState.Started, true)]
    [TestCase(6, 0, QuestState.Completed, false)]
    public void CanExpire(int weekday, int minutes, QuestState state, bool expected) {
        QuestMetadataBasic basic = Quest(QuestType.AllianceQuest, 3, 0, resetWeekday: weekday, durationMinutes: minutes).Basic;
        Assert.That(QuestRules.CanExpire(basic, state), Is.EqualTo(expected));
    }

    [Test]
    public void CanExpire_EventTag() {
        QuestMetadataBasic basic = Quest(QuestType.EventQuest, 0, 0).Basic with { EventTag = "event" };
        Assert.That(QuestRules.CanExpire(basic, QuestState.Started), Is.True);
    }

    // usePeriod: a day name is a reset weekday; anything else is read as the client's atoi reads it.
    [TestCase("fri", 6, 0)]
    [TestCase("FRI", 6, 0)]
    [TestCase("1440", 0, 1440)]
    [TestCase(" 60min", 0, 60)]
    [TestCase("", 0, 0)]
    [TestCase("x", 0, 0)]
    public void UsePeriod(string usePeriod, int weekday, int minutes) {
        Assert.Multiple(() => {
            Assert.That(QuestPeriod.ResetWeekday(usePeriod), Is.EqualTo(weekday));
            Assert.That(QuestPeriod.DurationMinutes(usePeriod), Is.EqualTo(minutes));
        });
    }

    private static QuestMetadata FameQuest(AllianceType alliance, int rank, int useMainFamePoint) {
        var basic = new QuestMetadataBasic(ChapterId: 0, Type: QuestType.AllianceQuest, Account: 0, StandardLevel: 1,
            Forfeitable: true, EventTag: string.Empty, AutoStart: false, Disabled: false, UsePostbox: false, StartNpc: 0,
            CompleteNpc: 0, CompleteMaps: null, ProgressMaps: null, Repeat: 2, Alliance: alliance, Rank: rank);
        var require = new QuestMetadataRequire(0, 0, [], [], [], 0, (0, 0), 0);
        var reward = new QuestMetadataReward(0, 0, ExpType.none, 0, 0, 0, 0, 0, 0, 0, [], [], useMainFamePoint);
        return new QuestMetadata(1, null, basic, require, null!, reward, null!, null!, null!, null!,
            null, null, null, QuestEventMissionType.none, []);
    }

    // The fame a completed mission gives: the reward's useMainFamePoint and an alliance are needed; rank D of a main
    // alliance gives 700 in KR (the window's +700), 90 elsewhere.
    [NonParallelizable]
    [TestCase("KR", AllianceType.DarkWind, 4, 1, 700)]
    [TestCase("KR", AllianceType.GreenHoods, 0, 2, 4000)]
    [TestCase("KR", AllianceType.KritiasGreenHoods, 4, 1, 90)]
    [TestCase("NA", AllianceType.DarkWind, 4, 1, 90)]
    [TestCase("NA", AllianceType.DarkWind, 1, 2, 2000)]
    [TestCase("KR", AllianceType.DarkWind, 4, 0, 0)]
    [TestCase("KR", AllianceType.AnyFaction, 4, 1, 0)]
    public void FameReward(string locale, AllianceType alliance, int rank, int useMainFamePoint, int expected) {
        string old = Maple2.Server.Game.LuaFunctions.Lua.Locale;
        Maple2.Server.Game.LuaFunctions.Lua.Locale = locale;
        try {
            Assert.That(AllianceMissions.FameReward(FameQuest(alliance, rank, useMainFamePoint)), Is.EqualTo(expected));
        } finally {
            Maple2.Server.Game.LuaFunctions.Lua.Locale = old;
        }
    }

    [TestCase(0, 700, 700)]
    [TestCase(700, 700, 1400)]
    [TestCase(int.MaxValue - 10, 700, int.MaxValue)]
    public void AddFame(int points, int amount, int expected) {
        Assert.That(AllianceMissions.AddFame(points, amount), Is.EqualTo(expected));
    }
}
