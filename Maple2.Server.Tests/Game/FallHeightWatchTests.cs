using System.Numerics;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game;

// The client reports the height from its fall top (where the jump or fall began) to where it lands
// (pcbase.cpp:4350-4373); both were positions it reported in its records. The server checks a height against the highest
// less the lowest z of the records since the last report, once a grace has passed.
public class FallHeightWatchTests {
    private static Vector3 At(float z) => new(0, 0, z);

    [Test]
    public void AHeightWithinTheRecords_IsNotFound() {
        var watch = new FallHeightWatch();
        watch.Seen(At(3000), 0);
        watch.Seen(At(1500), 100);
        Assert.That(watch.Fell(1500, At(1500), 200), Is.Null);
        Assert.That(watch.Seen(At(1500), 200 + FallHeightWatch.GraceMs), Is.Null);
    }

    [Test]
    public void AHeightAboveTheRecords_IsFoundOnceTheGraceIsOver() {
        var watch = new FallHeightWatch();
        watch.Seen(At(3000), 0);
        watch.Seen(At(1500), 100);
        Assert.That(watch.Fell(5000, At(1500), 200), Is.Null, "pending");
        Assert.That(watch.Seen(At(1500), 300), Is.Null, "still in the grace");

        FallHeightWatch.Excess? excess = watch.Seen(At(1500), 200 + FallHeightWatch.GraceMs);

        Assert.That(excess, Is.Not.Null);
        Assert.That(excess!.Value.Height, Is.EqualTo(5000));
        Assert.That(excess.Value.Allowed, Is.EqualTo(1500 + FallHeightWatch.Slack));
    }

    // The records of the top and the landing may come after the report: they count within the grace.
    [Test]
    public void RecordsWithinTheGrace_Count() {
        var watch = new FallHeightWatch();
        watch.Seen(At(1000), 0);
        watch.Fell(2000, At(1000), 100);
        watch.Seen(At(3000), 150);
        watch.Seen(At(1000), 200);
        Assert.That(watch.Seen(At(1000), 100 + FallHeightWatch.GraceMs), Is.Null);
    }

    [Test]
    public void TheSlack_CoversThePositionsBetweenRecords() {
        var watch = new FallHeightWatch();
        watch.Seen(At(2500), 0);
        watch.Seen(At(1000), 100);
        watch.Fell(1500 + FallHeightWatch.Slack, At(1000), 200);
        Assert.That(watch.Seen(At(1000), 200 + FallHeightWatch.GraceMs), Is.Null);
    }

    // Each report starts a new window: an old top does not count for the next fall, but the records of the grace do.
    [Test]
    public void AReport_StartsTheNextWindow() {
        var watch = new FallHeightWatch();
        watch.Seen(At(9000), 0);
        watch.Seen(At(1000), 100);
        watch.Fell(1500, At(1000), 200);
        watch.Seen(At(1200), 300);
        watch.Seen(At(1000), 200 + FallHeightWatch.GraceMs);

        watch.Fell(5000, At(1000), 5000);
        FallHeightWatch.Excess? excess = watch.Seen(At(1000), 5000 + FallHeightWatch.GraceMs);

        Assert.That(excess, Is.Not.Null);
        Assert.That(excess!.Value.Allowed, Is.EqualTo(200 + FallHeightWatch.Slack));
    }

    // A second report before the first's grace is over settles the first with what was seen.
    [Test]
    public void ASecondReport_SettlesTheFirst() {
        var watch = new FallHeightWatch();
        watch.Seen(At(1000), 0);
        watch.Fell(5000, At(1000), 100);
        Assert.That(watch.Fell(10, At(1000), 200), Is.Not.Null);
    }

    // A height or a position that is not a number is passed over; with no record at all there is nothing to measure by.
    [Test]
    public void NotANumber_OrNoRecord_IsNotFound() {
        var watch = new FallHeightWatch();
        Assert.Multiple(() => {
            Assert.That(watch.Fell(float.NaN, At(0), 0), Is.Null);
            Assert.That(watch.Seen(At(float.NaN), FallHeightWatch.GraceMs), Is.Null);
            Assert.That(new FallHeightWatch().Fell(5000, At(float.NaN), 0), Is.Null);
        });
        var empty = new FallHeightWatch();
        empty.Fell(5000, At(float.NaN), 0);
        Assert.That(empty.Seen(At(float.NaN), FallHeightWatch.GraceMs), Is.Null);
    }
}
