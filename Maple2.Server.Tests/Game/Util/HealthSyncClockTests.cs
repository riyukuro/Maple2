using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

public class HealthSyncClockTests {
    [Test]
    public void FirstChangeGoesAtOnce() {
        var clock = new HealthSyncClock();
        Assert.That(clock.Changed(0), Is.True);
        Assert.That(clock.Pending, Is.False);
    }

    [Test]
    public void ChangesInsideTheIntervalAreHeldAndSentOnce() {
        var clock = new HealthSyncClock();
        Assert.That(clock.Changed(1000), Is.True);
        int sent = 0;
        for (long tick = 1010; tick < 1000 + HealthSyncClock.IntervalTick; tick += 10) {
            if (clock.Changed(tick)) sent++;
            if (clock.Due(tick)) sent++;
        }
        Assert.That(sent, Is.EqualTo(0));
        Assert.That(clock.Pending, Is.True);

        Assert.That(clock.Due(1000 + HealthSyncClock.IntervalTick), Is.True);
        Assert.That(clock.Pending, Is.False);
        Assert.That(clock.Due(1000 + HealthSyncClock.IntervalTick + 10), Is.False);
    }

    [Test]
    public void BusyFightSendsAtMostOncePerInterval() {
        var clock = new HealthSyncClock();
        int sent = 0;
        // A hit every 20 ticks for ten seconds.
        for (long tick = 0; tick < 10_000; tick += 10) {
            if (tick % 20 == 0 && clock.Changed(tick)) sent++;
            if (clock.Due(tick)) sent++;
        }
        Assert.That(sent, Is.EqualTo(10_000 / HealthSyncClock.IntervalTick));
    }

    [Test]
    public void ForcedChangeGoesAtOnceAndClearsTheHeldOne() {
        var clock = new HealthSyncClock();
        clock.Changed(0);
        Assert.That(clock.Changed(100), Is.False);
        Assert.That(clock.Changed(120, force: true), Is.True);
        Assert.That(clock.Pending, Is.False);
        Assert.That(clock.Due(120 + HealthSyncClock.IntervalTick), Is.False);
    }

    [Test]
    public void AnotherSendClearsTheHeldChange() {
        var clock = new HealthSyncClock();
        clock.Changed(0);
        clock.Changed(100);
        clock.Sent(200);
        Assert.That(clock.Due(1000), Is.False);
        Assert.That(clock.Changed(300), Is.False);
        Assert.That(clock.Due(700), Is.True);
    }

    [Test]
    public void NoChangeNothingDue() {
        var clock = new HealthSyncClock();
        Assert.That(clock.Due(5000), Is.False);
    }
}
