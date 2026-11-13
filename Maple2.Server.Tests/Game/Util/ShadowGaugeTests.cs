using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

// The boss gauge's kills out of the monsters at its open (shadow_expedition_open_boss_gauge). Made-up object ids; 02000401_bf's most points.
public class ShadowGaugeTests {
    [Test]
    public void Kills_FillTheGaugeByShare() {
        var gauge = new ShadowGauge([1, 2, 3, 4], 1000);
        Assert.That(gauge.Points, Is.EqualTo(0));
        Assert.That(gauge.Killed(1), Is.EqualTo(250));
        Assert.That(gauge.Killed(2), Is.EqualTo(500));
        Assert.That(gauge.Killed(3), Is.EqualTo(750));
        Assert.That(gauge.Killed(4), Is.EqualTo(1000));
        Assert.That(gauge.Points, Is.EqualTo(1000));
    }

    [Test]
    public void ShareRoundsDown_AndTheLastKillGivesTheMost() {
        var gauge = new ShadowGauge([1, 2, 3], 1000);
        Assert.That(gauge.Killed(1), Is.EqualTo(333));
        Assert.That(gauge.Killed(2), Is.EqualTo(666));
        Assert.That(gauge.Killed(3), Is.EqualTo(1000));
    }

    [Test]
    public void Open_LeavesOutDeadMonstersAndPlayerSummons() {
        int[] counted = [.. ShadowGauge.AtOpen([(1, false, false), (2, true, false), (3, false, true), (4, false, false)])];
        Assert.That(counted, Is.EqualTo(new[] { 1, 4 }));
        var gauge = new ShadowGauge(counted, 1000);
        Assert.That(gauge.Killed(3), Is.Null);
        Assert.That(gauge.Killed(1), Is.EqualTo(500));
    }

    [Test]
    public void UnknownDeath_ChangesNothing() {
        // A friendly npc or a player's summon is never handed to Count.
        var gauge = new ShadowGauge([1, 2], 1000);
        Assert.That(gauge.Killed(9), Is.Null);
        Assert.That(gauge.Killed(1), Is.EqualTo(500));
        Assert.That(gauge.Killed(1), Is.Null);
        Assert.That(gauge.Points, Is.EqualTo(500));
    }

    [Test]
    public void SpawnBeforeTheFirstKill_JoinsTheCount() {
        // The scripts open the gauge and spawn the wave in one step.
        var gauge = new ShadowGauge([1], 1000);
        gauge.Count(2);
        Assert.That(gauge.Killed(1), Is.EqualTo(500));
        Assert.That(gauge.Killed(2), Is.EqualTo(1000));
    }

    [Test]
    public void RefilledMonsters_FillTheGauge_WhileOthersLive() {
        // 02000401_bf's spawn points refill every 10 s: the count stays at the open's 4.
        var gauge = new ShadowGauge([1, 2, 3, 4], 1000);
        Assert.That(gauge.Killed(1), Is.EqualTo(250));
        Assert.That(gauge.Killed(2), Is.EqualTo(500));
        gauge.Count(5);
        gauge.Count(6);
        Assert.That(gauge.Killed(5), Is.EqualTo(750));
        Assert.That(gauge.Killed(6), Is.EqualTo(1000));
        Assert.That(gauge.Killed(3), Is.Null);
    }

    [Test]
    public void DestroyedMonster_LeavesTheCount() {
        // 02000401_bf destroys 2002 in the tick it opens the gauge: the removal comes after the open.
        var gauge = new ShadowGauge([1, 2, 3, 4], 1000);
        Assert.That(gauge.Removed(4), Is.Null);
        Assert.That(gauge.Killed(1), Is.EqualTo(333));
        Assert.That(gauge.Removed(3), Is.EqualTo(500));
        Assert.That(gauge.Killed(2), Is.EqualTo(1000));
        // A killed monster's later removal from the field changes nothing.
        Assert.That(gauge.Removed(2), Is.Null);
    }

    [Test]
    public void EmptyField_NeedsAKill() {
        var gauge = new ShadowGauge([], 300);
        Assert.That(gauge.Removed(1), Is.Null);
        gauge.Count(5);
        Assert.That(gauge.Killed(5), Is.EqualTo(300));
    }
}
