using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Maple2.Model.Game;
using Maple2.Model.Metadata;

namespace Maple2.Server.Tests.Game;

// WorldChampionBase.xml: a season runs from activeBegin to activeEnd (KR season 1: 2018-05-11-00-00-00 to
// 2018-05-13-23-59-59), its steps' npcs come out one after the other, and the rewards of the steps cleared are
// mailed in the reward days after the end (constants worldChampionRewardDays 7).
public class WorldChampionRulesTests {
    private static WorldChampionTable.Season Season(int id, DateTime begin, int steps = 3, long rewardDamage = 1500000) {
        return new WorldChampionTable.Season(id, 2000423, 100, begin, begin.AddDays(3).AddSeconds(-1), rewardDamage, 180510006,
            Enumerable.Range(1, steps).Select(step => new WorldChampionTable.Step(step, 23500109 + step, 20000000 + step)).ToList());
    }

    private static readonly DateTime Friday = new(2026, 11, 13, 0, 0, 0);

    private static WorldChampionTable Table(params WorldChampionTable.Season[] seasons) {
        return new WorldChampionTable(seasons.ToDictionary(season => season.Id));
    }

    [Test]
    public void Current_BeginAndEndIncluded() {
        WorldChampionTable table = Table(Season(1, Friday), Season(2, Friday.AddDays(7)));
        Assert.Multiple(() => {
            Assert.That(WorldChampionRules.Current(table, Friday.AddSeconds(-1)), Is.Null);
            Assert.That(WorldChampionRules.Current(table, Friday)?.Id, Is.EqualTo(1));
            Assert.That(WorldChampionRules.Current(table, Friday.AddDays(3).AddSeconds(-1))?.Id, Is.EqualTo(1));
            Assert.That(WorldChampionRules.Current(table, Friday.AddDays(3)), Is.Null);
            Assert.That(WorldChampionRules.Current(table, Friday.AddDays(8))?.Id, Is.EqualTo(2));
        });
    }

    [Test]
    public void Current_LowerIdOfTwo() {
        WorldChampionTable table = Table(Season(4, Friday), Season(3, Friday.AddDays(-1)));
        Assert.That(WorldChampionRules.Current(table, Friday.AddHours(1))?.Id, Is.EqualTo(3));
    }

    [Test]
    public void Next_TheNextToBegin() {
        WorldChampionTable table = Table(Season(1, Friday), Season(3, Friday.AddDays(14)), Season(2, Friday.AddDays(7)));
        Assert.Multiple(() => {
            Assert.That(WorldChampionRules.Next(table, Friday.AddDays(-1))?.Id, Is.EqualTo(1));
            Assert.That(WorldChampionRules.Next(table, Friday)?.Id, Is.EqualTo(2));
            Assert.That(WorldChampionRules.Next(table, Friday.AddDays(4))?.Id, Is.EqualTo(2));
            // After the last begin: the last season's week rolled forward (WorldChampion_RollsTheLastWeek).
            Assert.That(WorldChampionRules.Next(table, Friday.AddDays(14))?.Id, Is.EqualTo(4));
            Assert.That(WorldChampionRules.Next(table, Friday.AddDays(14))?.Begin, Is.EqualTo(Friday.AddDays(21)));
        });
    }

    // KR's last seasons: Saturday and Sunday 19:00-22:00 over two weeks (ids 5-8). Only the last week (7 and 8)
    // repeats, every week after it, ids from 9 on; Find gives the same season by its id.
    private static WorldChampionTable WeekendTable() {
        DateTime saturday = new(2020, 7, 11, 19, 0, 0);
        WorldChampionTable.Season Evening(int id, DateTime begin) => Season(id, begin) with { End = begin.AddHours(3) };
        return Table(Evening(5, saturday), Evening(6, saturday.AddDays(1)), Evening(7, saturday.AddDays(7)), Evening(8, saturday.AddDays(8)));
    }

    [Test]
    public void WorldChampion_RollsTheLastWeek() {
        WorldChampionTable table = WeekendTable();
        DateTime saturday = new(2020, 7, 25, 19, 0, 0);
        Assert.Multiple(() => {
            Assert.That(WorldChampionRules.Current(table, saturday.AddHours(1))?.Id, Is.EqualTo(9));
            Assert.That(WorldChampionRules.Current(table, saturday.AddDays(1).AddHours(1))?.Id, Is.EqualTo(10));
            Assert.That(WorldChampionRules.Current(table, saturday.AddDays(7).AddHours(1))?.Id, Is.EqualTo(11));
            Assert.That(WorldChampionRules.Current(table, saturday.AddHours(4)), Is.Null);
            Assert.That(WorldChampionRules.Current(table, saturday.AddDays(-1)), Is.Null);
            Assert.That(WorldChampionRules.Next(table, saturday.AddHours(4))?.Id, Is.EqualTo(10));
            Assert.That(WorldChampionRules.Next(table, saturday.AddDays(1).AddHours(4))?.Begin, Is.EqualTo(saturday.AddDays(7)));
            WorldChampionTable.Season? rolled = WorldChampionRules.Current(table, saturday.AddHours(1));
            Assert.That(WorldChampionRules.Find(table, 9), Is.EqualTo(rolled));
            Assert.That(rolled!.Steps, Is.EqualTo(table.Entries[7].Steps));
            Assert.That(rolled.End, Is.EqualTo(saturday.AddHours(3)));
            Assert.That(WorldChampionRules.Find(table, 7), Is.SameAs(table.Entries[7]));
            Assert.That(WorldChampionRules.Find(table, 4), Is.Null);
            Assert.That(WorldChampionRules.Find(table, 0), Is.Null);
            Assert.That(WorldChampionRules.Find(Table(), 9), Is.Null);
        });
    }

    // Years on: the season on is the week's, found again by its id, still Saturday 19:00.
    [Test]
    public void WorldChampion_RollsYearsOn() {
        WorldChampionTable table = WeekendTable();
        DateTime saturday = new(2026, 11, 14, 19, 0, 0);
        WorldChampionTable.Season? season = WorldChampionRules.Current(table, saturday.AddHours(2));
        Assert.Multiple(() => {
            Assert.That(season?.Begin, Is.EqualTo(saturday));
            Assert.That(WorldChampionRules.Find(table, season!.Id), Is.EqualTo(season));
            Assert.That(WorldChampionRules.Current(table, saturday.AddDays(1).AddHours(2))?.Id, Is.EqualTo(season.Id + 1));
        });
    }

    // Seasons of different lengths: a long one still on from an earlier week is found (the roll starts at the week of
    // the latest end, not the earliest).
    [Test]
    public void WorldChampion_RollsLongSeason() {
        DateTime saturday = new(2020, 7, 11, 19, 0, 0);
        WorldChampionTable table = Table(Season(1, saturday) with { End = saturday.AddHours(3) },
            Season(2, saturday.AddDays(1)) with { End = saturday.AddDays(10) });
        // Week 1: 3 (Saturday + 7) and 4 (Sunday + 7 to Saturday + 17); week 2's 6 begins Sunday + 14.
        Assert.That(WorldChampionRules.Current(table, saturday.AddDays(16))?.Id, Is.EqualTo(4));
    }

    // A rolled season's rewards are due as a table season's.
    [Test]
    public void RewardDue_RolledSeason() {
        WorldChampionTable table = WeekendTable();
        DateTime end = new(2020, 7, 25, 22, 0, 0);
        Assert.Multiple(() => {
            Assert.That(WorldChampionRules.RewardDue(table, end.AddMinutes(2), 7).Select(s => s.Id), Is.EqualTo(new[] { 8, 9 }));
            Assert.That(WorldChampionRules.RewardDue(table, end.AddDays(2), 7).Select(s => s.Id), Is.EqualTo(new[] { 9, 10 }));
            Assert.That(WorldChampionRules.RewardDue(table, end.AddDays(7).AddMinutes(2), 7).Select(s => s.Id), Is.EqualTo(new[] { 10, 11 }));
        });
    }

    // Due a minute after the end (the World server's last save), for the reward days (0 = the default 7).
    [Test]
    public void RewardDue_TheDaysAfterTheEnd() {
        WorldChampionTable table = Table(Season(1, Friday));
        DateTime end = Friday.AddDays(3).AddSeconds(-1);
        Assert.Multiple(() => {
            Assert.That(WorldChampionRules.RewardDue(table, end, 7), Is.Empty);
            Assert.That(WorldChampionRules.RewardDue(table, end.AddMinutes(1), 7), Is.Empty);
            Assert.That(WorldChampionRules.RewardDue(table, end.AddMinutes(1).AddSeconds(1), 7).Select(s => s.Id), Is.EqualTo(new[] { 1 }));
            Assert.That(WorldChampionRules.RewardDue(table, end.AddDays(7), 7).Select(s => s.Id), Is.EqualTo(new[] { 1 }));
            Assert.That(WorldChampionRules.RewardDue(table, end.AddDays(7).AddSeconds(1), 7), Is.Empty);
            Assert.That(WorldChampionRules.RewardDue(table, end.AddDays(2), 1), Is.Empty);
            Assert.That(WorldChampionRules.RewardDue(table, end.AddDays(6), 0).Select(s => s.Id), Is.EqualTo(new[] { 1 }));
        });
    }

    [Test]
    public void Apply_StepsOn() {
        WorldChampionTable.Season season = Season(1, Friday);
        Assert.Multiple(() => {
            Assert.That(WorldChampionRules.Apply(season, 1, 0, 1, 400, 1000), Is.EqualTo((1, 400L)));
            Assert.That(WorldChampionRules.Apply(season, 1, 400, 1, 600, 1000), Is.EqualTo((2, 0L)));
            // Past the health: the rest is not carried to the next npc.
            Assert.That(WorldChampionRules.Apply(season, 1, 900, 1, 5000, 1000), Is.EqualTo((2, 0L)));
            // The last step cleared: the season is cleared.
            Assert.That(WorldChampionRules.Apply(season, 3, 0, 3, 1000, 1000), Is.EqualTo((4, 0L)));
        });
    }

    // Damage to a step another channel already cleared, to a cleared season, or none, changes nothing.
    [Test]
    public void Apply_Ignored() {
        WorldChampionTable.Season season = Season(1, Friday);
        Assert.Multiple(() => {
            Assert.That(WorldChampionRules.Apply(season, 2, 100, 1, 900, 1000), Is.EqualTo((2, 100L)));
            Assert.That(WorldChampionRules.Apply(season, 4, 0, 4, 900, 1000), Is.EqualTo((4, 0L)));
            Assert.That(WorldChampionRules.Apply(season, 1, 100, 1, 0, 1000), Is.EqualTo((1, 100L)));
            Assert.That(WorldChampionRules.Apply(season, 1, 100, 1, -5, 1000), Is.EqualTo((1, 100L)));
        });
    }

    [Test]
    public void ClearedAndStepOf() {
        WorldChampionTable.Season season = Season(1, Friday);
        Assert.Multiple(() => {
            Assert.That(WorldChampionRules.Cleared(season, 3), Is.False);
            Assert.That(WorldChampionRules.Cleared(season, 4), Is.True);
            Assert.That(WorldChampionRules.StepOf(season, 1)?.NpcId, Is.EqualTo(23500110));
            Assert.That(WorldChampionRules.StepOf(season, 3)?.NpcId, Is.EqualTo(23500112));
            Assert.That(WorldChampionRules.StepOf(season, 4), Is.Null);
            Assert.That(WorldChampionRules.StepOf(season, 0), Is.Null);
        });
    }

    [Test]
    public void Health_NeverUnderOne() {
        Assert.Multiple(() => {
            Assert.That(WorldChampionRules.Health(1000, 0), Is.EqualTo(1000));
            Assert.That(WorldChampionRules.Health(1000, 400), Is.EqualTo(600));
            Assert.That(WorldChampionRules.Health(1000, 1000), Is.EqualTo(1));
            Assert.That(WorldChampionRules.Health(1000, 5000), Is.EqualTo(1));
            Assert.That(WorldChampionRules.Health(1000, -5), Is.EqualTo(1000));
        });
    }

    // The row of the last step cleared (s_world_champion_reward_title: the rewards up to the step).
    [Test]
    public void RewardId_LastStepCleared() {
        WorldChampionTable.Season season = Season(1, Friday);
        Assert.Multiple(() => {
            Assert.That(WorldChampionRules.RewardId(season, 1), Is.EqualTo(0));
            Assert.That(WorldChampionRules.RewardId(season, 2), Is.EqualTo(20000001));
            Assert.That(WorldChampionRules.RewardId(season, 3), Is.EqualTo(20000002));
            Assert.That(WorldChampionRules.RewardId(season, 4), Is.EqualTo(20000003));
            Assert.That(WorldChampionRules.RewardId(season, 9), Is.EqualTo(20000003));
        });
    }

    [Test]
    public void Rewarded_AtTheSeasonDamage() {
        WorldChampionTable.Season season = Season(1, Friday, rewardDamage: 1500000);
        Assert.Multiple(() => {
            Assert.That(WorldChampionRules.Rewarded(season, 1499999), Is.False);
            Assert.That(WorldChampionRules.Rewarded(season, 1500000), Is.True);
            Assert.That(WorldChampionRules.Rewarded(Season(2, Friday, rewardDamage: 0), 0), Is.False);
            Assert.That(WorldChampionRules.Rewarded(Season(2, Friday, rewardDamage: 0), 1), Is.True);
        });
    }
}
