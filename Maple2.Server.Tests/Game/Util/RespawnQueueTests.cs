using System.Collections.Generic;
using Maple2.Server.Game.Util;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game.Util;

public class RespawnQueueTests {
    private const float DeadTime = 5; // npc Dead.Time of the lv10-20 field mobs
    private const int Cooldown = 13;  // map spawn cooldown
    private const int Population = 4;

    // Runs a group of Population from 0 to end in 100 ms steps with deaths at the given ticks; returns the respawn ticks.
    private static List<long> Run(long end, params long[] deaths) {
        var queue = new RespawnQueue();
        int alive = Population;
        var spawns = new List<long>();
        for (long tick = 0; tick <= end; tick += 100) {
            foreach (long death in deaths) {
                if (death == tick) {
                    alive--;
                    queue.Died(tick, DeadTime, Cooldown);
                }
            }
            int count = queue.Take(tick, Population - alive);
            for (int i = 0; i < count; i++) {
                spawns.Add(tick);
            }
            alive += count;
            Assert.That(alive, Is.LessThanOrEqualTo(Population));
        }
        return spawns;
    }

    [Test]
    public void OneDeath_OneMobBackAfterDeadTimePlusCooldown() {
        Assert.That(Run(60_000, 10_000), Is.EqualTo(new long[] { 28_000 }));
    }

    [Test]
    public void TwoDeathsApart_EachComesBackOnItsOwnTimer() {
        Assert.That(Run(60_000, 10_000, 15_000), Is.EqualTo(new long[] { 28_000, 33_000 }));
    }

    [Test]
    public void ClearedGroup_EachMobBack18sAfterItsDeath() {
        Assert.That(Run(60_000, 1_000, 2_000, 3_000, 4_000), Is.EqualTo(new long[] { 19_000, 20_000, 21_000, 22_000 }));
    }

    [Test]
    public void RecordedDelays_PerCooldown() {
        foreach ((int cooldown, long delay) in new[] { (13, 18_000L), (20, 25_000L), (30, 35_000L) }) {
            var queue = new RespawnQueue();
            queue.Died(0, DeadTime, cooldown);
            Assert.That(queue.Take(delay - 1, 1), Is.Zero);
            Assert.That(queue.Take(delay, 1), Is.EqualTo(1));
        }
    }

    [Test]
    public void NoCooldown_NoRespawn() {
        var queue = new RespawnQueue();
        queue.Died(0, DeadTime, 0);
        Assert.That(queue.Count, Is.Zero);
    }

    [Test]
    public void FullGroup_KeepsTheTimerUntilThereIsRoom() {
        var queue = new RespawnQueue();
        queue.Died(0, DeadTime, Cooldown);
        Assert.That(queue.Take(20_000, 0), Is.Zero);
        Assert.That(queue.Count, Is.EqualTo(1));
        Assert.That(queue.Take(21_000, 1), Is.EqualTo(1));
    }

    [Test]
    public void DebugRate_TwiceAsFast() {
        var queue = new RespawnQueue();
        queue.Died(0, DeadTime, Cooldown, msPerSecond: 500);
        Assert.That(queue.Take(9_000, 1), Is.EqualTo(1));
    }
}
