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

    [Test]
    public void EndResetFillsToTotal() {
        Stats stats = Hurt(40);
        stats.Reset(new Dictionary<BasicAttribute, long> { { BasicAttribute.Health, 150 } }, JobCode.Newbie);
        stats.Total();
        stats.EndReset(false);
        Assert.That(stats[BasicAttribute.Health].Current, Is.EqualTo(150));

        stats[BasicAttribute.Health].Add(-200);
        Assert.That(stats[BasicAttribute.Health].Current, Is.EqualTo(0));
        stats[BasicAttribute.Health].Add(500);
        Assert.That(stats[BasicAttribute.Health].Current, Is.EqualTo(150), "clamped to Total again after EndReset");
    }

    [Test]
    public void EndResetKeepsDeadAtZero() {
        Stats stats = Hurt(40);
        stats.Reset(new Dictionary<BasicAttribute, long> { { BasicAttribute.Health, 150 } }, JobCode.Newbie);
        stats.Total();
        stats.EndReset(true);
        Assert.That(stats[BasicAttribute.Health].Current, Is.EqualTo(0));
        Assert.That(stats[BasicAttribute.Health].Total, Is.EqualTo(150));
    }

    [Test]
    public void SetStaticStatsEndsFull() {
        Stats stats = Hurt(40);
        stats.SetStaticStats(new Dictionary<BasicAttribute, long> { { BasicAttribute.Health, 300 } });
        Assert.That(stats[BasicAttribute.Health].Current, Is.EqualTo(300));
        stats[BasicAttribute.Health].Add(1000);
        Assert.That(stats[BasicAttribute.Health].Current, Is.EqualTo(300));
    }
}
