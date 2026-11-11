using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
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

    private static QuestMetadata Quest(QuestType type, int repeat, int groupId) {
        var basic = new QuestMetadataBasic(ChapterId: 0, Type: type, Account: 0, StandardLevel: 1, Forfeitable: true,
            EventTag: string.Empty, AutoStart: false, Disabled: false, UsePostbox: false, StartNpc: 0, CompleteNpc: 0,
            CompleteMaps: null, ProgressMaps: null, Repeat: repeat);
        var require = new QuestMetadataRequire(0, 0, [], [], [], 0, (0, 0), 0, groupId);
        return new QuestMetadata(1, null, basic, require, null!, null!, null!, null!, null!, null!,
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
}
