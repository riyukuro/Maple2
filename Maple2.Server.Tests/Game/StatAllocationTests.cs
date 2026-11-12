using System.Collections.Generic;
using Maple2.Model.Enum;
using Maple2.Server.Core.Formulas;
using Maple2.Server.Game.Model;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game;

// Spent attribute points are added on every stat rebuild, and the attack and resistance follow the total
// str, dex, int and luk (base value from the base stats).
public class StatAllocationTests {
    private static readonly Dictionary<BasicAttribute, long> RuneBlader12 = new() {
        { BasicAttribute.Strength, 85 },
        { BasicAttribute.Dexterity, 17 },
        { BasicAttribute.Intelligence, 7 },
        { BasicAttribute.Luck, 7 },
        { BasicAttribute.Health, 460 },
        { BasicAttribute.CriticalRate, 45 },
    };

    // Base str, dex, int, luk and the base physical attack, magical attack, physical and magical resistance in KMS2.
    [TestCase(JobCode.RuneBlader, 85, 17, 7, 56, 3, 12, 1)]
    [TestCase(JobCode.Knight, 381, 67, 26, 251, 9, 52, 14)]
    [TestCase(JobCode.Striker, 12, 50, 4, 33, 1, 7, 2)]
    [TestCase(JobCode.Wizard, 7, 6, 142, 4, 80, 0, 15)]
    [TestCase(JobCode.Wizard, 12, 12, 270, 8, 153, 0, 29)]
    [TestCase(JobCode.Priest, 14, 14, 326, 8, 154, 0, 35)]
    [TestCase(JobCode.SoulBinder, 18, 17, 438, 13, 248, 0, 48)]
    public void BaseValues(JobCode job, int str, int dex, int @int, int patk, int matk, int pres, int mres) {
        var stats = new Stats(new Dictionary<BasicAttribute, long> {
            { BasicAttribute.Strength, str },
            { BasicAttribute.Dexterity, dex },
            { BasicAttribute.Intelligence, @int },
        }, job);
        // The casters' physical attack and resistance are not settled: the expected values there are the server's own.
        Assert.That(stats[BasicAttribute.PhysicalAtk].Base, Is.EqualTo(patk));
        Assert.That(stats[BasicAttribute.MagicalAtk].Base, Is.EqualTo(matk));
        Assert.That(stats[BasicAttribute.PhysicalRes].Base, Is.EqualTo(pres));
        Assert.That(stats[BasicAttribute.MagicalRes].Base, Is.EqualTo(mres));
        Assert.That(stats[BasicAttribute.PhysicalAtk].Total, Is.EqualTo(patk));
    }

    [Test]
    public void LowStatsGiveAtLeastOneAttack() {
        Assert.That(AttackStat.PhysicalAtk(JobCode.Newbie, 1, 1, 1), Is.EqualTo(1));
        Assert.That(AttackStat.MagicalAtk(JobCode.Knight, 2), Is.EqualTo(1));
        Assert.That(AttackStat.MagicalAtk(JobCode.Knight, 26), Is.EqualTo(9));
    }

    [Test]
    public void OlderValuesKeptForUnsettledJobs() {
        Assert.That(AttackStat.PhysicalAtk(JobCode.Thief, 67, 27, 381), Is.EqualTo(252));
        Assert.That(AttackStat.MagicalAtk(JobCode.Archer, 30), Is.EqualTo(19));
        Assert.That(AttackStat.PhysicalRes(JobCode.Thief, 67, 27), Is.EqualTo(0));
        Assert.That(AttackStat.MagicalRes(JobCode.Assassin, 25), Is.EqualTo(0));
    }

    private static Stats Rebuild(int str, int health, int crit, int gearStr, int gearInt) {
        var stats = new Stats(RuneBlader12, JobCode.RuneBlader);
        stats.Reset(RuneBlader12, JobCode.RuneBlader);
        stats.AddAllocation(BasicAttribute.Strength, str);
        stats.AddAllocation(BasicAttribute.Health, health);
        stats.AddAllocation(BasicAttribute.CriticalRate, crit);
        stats[BasicAttribute.Strength].AddTotal(gearStr);
        stats[BasicAttribute.Intelligence].AddTotal(gearInt);
        stats.AddDerivedTotal(JobCode.RuneBlader);
        stats.Total();
        stats.EndReset(false);
        return stats;
    }

    [Test]
    public void AllocationSurvivesRebuild() {
        Stats stats = Rebuild(str: 10, health: 2, crit: 3, gearStr: 0, gearInt: 0);
        Assert.That(stats[BasicAttribute.Strength].Total, Is.EqualTo(95));
        Assert.That(stats[BasicAttribute.Strength].Base, Is.EqualTo(85));
        Assert.That(stats[BasicAttribute.Health].Total, Is.EqualTo(480));
        Assert.That(stats[BasicAttribute.CriticalRate].Total, Is.EqualTo(54));
    }

    [Test]
    public void AttackFollowsTotalStats() {
        // floor(0.63 x 95 + 0.175 x 17) = 62, base stays floor(0.63 x 85 + 0.175 x 17) = 56.
        Stats spent = Rebuild(str: 10, health: 0, crit: 0, gearStr: 0, gearInt: 0);
        Assert.That(spent[BasicAttribute.PhysicalAtk].Base, Is.EqualTo(56));
        Assert.That(spent[BasicAttribute.PhysicalAtk].Total, Is.EqualTo(62));
        Assert.That(spent[BasicAttribute.PhysicalRes].Total, Is.EqualTo(13));

        // Item str and int count the same way: floor(0.63 x 105 + 2.975) = 69, floor(0.567 x 27) = 15, floor(0.22 x 27) = 5.
        Stats geared = Rebuild(str: 10, health: 0, crit: 0, gearStr: 10, gearInt: 20);
        Assert.That(geared[BasicAttribute.PhysicalAtk].Total, Is.EqualTo(69));
        Assert.That(geared[BasicAttribute.MagicalAtk].Base, Is.EqualTo(3));
        Assert.That(geared[BasicAttribute.MagicalAtk].Total, Is.EqualTo(15));
        Assert.That(geared[BasicAttribute.MagicalRes].Total, Is.EqualTo(5));
    }

    [Test]
    public void ResetDropsAllocationUntilAddedAgain() {
        Stats stats = Rebuild(str: 10, health: 0, crit: 0, gearStr: 0, gearInt: 0);
        stats.Reset(RuneBlader12, JobCode.RuneBlader);
        stats.AddDerivedTotal(JobCode.RuneBlader);
        stats.EndReset(false);
        Assert.That(stats[BasicAttribute.Strength].Total, Is.EqualTo(85));
        Assert.That(stats[BasicAttribute.PhysicalAtk].Total, Is.EqualTo(56));
    }
}
