using System.Collections.Generic;
using Maple2.Model.Enum;
using Maple2.Server.Game.Model;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game;

// Stats.Reset runs on a session thread while the field thread reads Health.Current for its death check,
// so no step of the rebuild may show a living player at 0 Current.
public class StatsResetTests {
    private static Stats Hurt(long current) {
        var stats = new Stats(new Dictionary<BasicAttribute, long> {
            { BasicAttribute.Health, 100 },
            { BasicAttribute.Strength, 5 },
        }, JobCode.Newbie);
        stats[BasicAttribute.Health].Add(current - stats[BasicAttribute.Health].Current);
        Assert.That(stats[BasicAttribute.Health].Current, Is.EqualTo(current));
        return stats;
    }

    [Test]
    public void ResetKeepsCurrentAtEachStep() {
        Stats stats = Hurt(40);
        Stat health = stats[BasicAttribute.Health];

        stats.Reset(new Dictionary<BasicAttribute, long> {
            { BasicAttribute.Health, 150 },
            { BasicAttribute.Strength, 7 },
        }, JobCode.Newbie);
        Assert.That(health.Current, Is.EqualTo(40), "after Reset");

        health.AddTotal(50);
        Assert.That(health.Current, Is.EqualTo(40), "after AddTotal");

        stats.Total();
        Assert.That(health.Current, Is.EqualTo(40), "after Total");
        Assert.That(health.Total, Is.EqualTo(200));
    }

    [Test]
    public void DamageDuringResetIsNotCappedByPartialTotal() {
        Stats stats = Hurt(40);
        Stat health = stats[BasicAttribute.Health];

        stats.Reset(new Dictionary<BasicAttribute, long> { { BasicAttribute.Health, 10 } }, JobCode.Newbie);
        health.Add(-5);
        Assert.That(health.Current, Is.EqualTo(35));
    }

    // One rebuild as StatsManager.Refresh does it: level stats, then the extra Total from equips, pets and buffs.
    private static void Rebuild(Stats stats, long baseHealth, long extraHealth, bool dead = false, bool levelUp = false) {
        stats.Reset(new Dictionary<BasicAttribute, long> {
            { BasicAttribute.Health, baseHealth },
            { BasicAttribute.Spirit, 100 },
        }, JobCode.Newbie);
        stats[BasicAttribute.Health].AddTotal(extraHealth);
        stats.Total();
        stats.EndReset(dead, levelUp);
    }

    private static Stats At(long baseHealth, long extraHealth, long current) {
        var stats = new Stats(new Dictionary<BasicAttribute, long> { { BasicAttribute.Health, 1 } }, JobCode.Newbie);
        Rebuild(stats, baseHealth, extraHealth);
        Stat health = stats[BasicAttribute.Health];
        health.Add(current - health.Current);
        Assert.That((health.Total, health.Current), Is.EqualTo((baseHealth + extraHealth, current)));
        return stats;
    }

    [Test]
    public void BuffWithoutHealthKeepsCurrent() {
        Stats stats = At(2740, 55, 2706);
        stats[BasicAttribute.Spirit].Add(-40);
        Rebuild(stats, 2740, 55);
        Assert.That(stats[BasicAttribute.Health].Current, Is.EqualTo(2706));
        Assert.That(stats[BasicAttribute.Spirit].Current, Is.EqualTo(60));
    }

    [Test]
    public void EquipScalesCurrentWithTotal() {
        // KMS2: 5758 / 5951 -> 6357 / 6571 after an equip.
        Stats stats = At(3307, 2644, 5758);
        Rebuild(stats, 3307, 3264);
        Assert.That(stats[BasicAttribute.Health].Current, Is.EqualTo(6357));
    }

    [Test]
    public void PetSummonAndSwapScaleBothWays() {
        // KMS2: 5640 / 7158 -> 5666 / 7192 with the pet's stats, then 5639 / 7158 without them.
        Stats stats = At(3083, 4075, 5640);
        Rebuild(stats, 3083, 4109);
        Assert.That(stats[BasicAttribute.Health].Current, Is.EqualTo(5666));
        Rebuild(stats, 3083, 4075);
        Assert.That(stats[BasicAttribute.Health].Current, Is.EqualTo(5639));
    }

    [Test]
    public void BuffLostClampsAFullBar() {
        Stats stats = At(3000, 500, 3500);
        Rebuild(stats, 3000, 0);
        Assert.That(stats[BasicAttribute.Health].Current, Is.EqualTo(3000));
    }

    [Test]
    public void LevelUpFillsHealthAndSpirit() {
        // KMS2: 2549 / 2795, level up, 2806 / 2806 and Spirit 100 / 100.
        Stats stats = At(2740, 55, 2549);
        stats[BasicAttribute.Spirit].Add(-30);
        Rebuild(stats, 2751, 55, levelUp: true);
        Assert.That(stats[BasicAttribute.Health].Current, Is.EqualTo(2806));
        Assert.That(stats[BasicAttribute.Spirit].Current, Is.EqualTo(100));
    }

    [Test]
    public void DeadStaysAtZero() {
        Stats stats = At(3000, 0, 0);
        Rebuild(stats, 3500, 0, dead: true, levelUp: true);
        Assert.That(stats[BasicAttribute.Health].Current, Is.EqualTo(0));
        Assert.That(stats[BasicAttribute.Health].Total, Is.EqualTo(3500));
    }

    [Test]
    public void SetStaticStatsScales() {
        Stats stats = At(100, 0, 40);
        stats.SetStaticStats(new Dictionary<BasicAttribute, long> { { BasicAttribute.Health, 300 } });
        Assert.That(stats[BasicAttribute.Health].Current, Is.EqualTo(120));
        stats[BasicAttribute.Health].Add(1000);
        Assert.That(stats[BasicAttribute.Health].Current, Is.EqualTo(300), "clamped to Total again after the rebuild");
    }
}
