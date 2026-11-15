using System.Linq;
using System.Numerics;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game;

// The client reports the height from its fall top (where the jump or fall began) to where it lands
// (pcbase.cpp:4350-4373); both were positions it reported in its records, which may come after the report. The server
// checks a height against the highest less the lowest z of the records since the last report, at the first record packet
// after one that came when the grace was over.
public class FallHeightWatchTests {
    private const long Grace = FallHeightWatch.GraceMs;

    private static Vector3 At(float z) => new(0, 0, z);

    private static FallHeightWatch.Excess[] Seen(FallHeightWatch watch, long now, params float[] zs) =>
        watch.Seen(zs.Select(At), now).ToArray();

    [Test]
    public void AHeightWithinTheRecords_IsNotFound() {
        var watch = new FallHeightWatch();
        Seen(watch, 0, 3000, 1500);
        watch.Fell(1500, At(1500), 200);
        Assert.That(Seen(watch, 200 + Grace, 1500), Is.Empty);
        Assert.That(Seen(watch, 300 + Grace, 1500), Is.Empty);
    }

    [Test]
    public void AHeightAboveTheRecords_IsFoundAtThePacketAfterTheGrace() {
        var watch = new FallHeightWatch();
        Seen(watch, 0, 3000, 1500);
        watch.Fell(5000, At(1500), 200);
        Assert.That(Seen(watch, 300, 1500), Is.Empty, "still in the grace");
        Assert.That(Seen(watch, 200 + Grace, 1500), Is.Empty, "the grace is over: one packet more");

        FallHeightWatch.Excess[] excess = Seen(watch, 300 + Grace, 1500);

        Assert.That(excess, Has.Length.EqualTo(1));
        Assert.That(excess[0].Height, Is.EqualTo(5000));
        Assert.That(excess[0].Allowed, Is.EqualTo(1500 + FallHeightWatch.Slack));
    }

    // The records of the top and the landing may come after the report: they count within the grace.
    [Test]
    public void RecordsWithinTheGrace_Count() {
        var watch = new FallHeightWatch();
        Seen(watch, 0, 1000);
        watch.Fell(2000, At(1000), 100);
        Seen(watch, 150, 3000);
        Seen(watch, 200, 1000);
        Seen(watch, 100 + Grace, 1000);
        Assert.That(Seen(watch, 200 + Grace, 1000), Is.Empty);
    }

    // A hitch after landing: the records in the air and the landing come in one late packet, or the landing in the
    // packet after it. Both are seen before the height is settled.
    [TestCase(false)]
    [TestCase(true)]
    public void ALateBatch_IsSeenWhole(bool landingInTheNextPacket) {
        var watch = new FallHeightWatch();
        Seen(watch, 0, 5000);
        watch.Fell(4000, At(5000), 100);
        if (landingInTheNextPacket) {
            Assert.That(Seen(watch, 100 + Grace + 500, 4500, 3000), Is.Empty);
            Assert.That(Seen(watch, 100 + Grace + 600, 1000), Is.Empty);
        } else {
            Assert.That(Seen(watch, 100 + Grace + 500, 4500, 3000, 1000), Is.Empty);
            Assert.That(Seen(watch, 100 + Grace + 600, 1000), Is.Empty);
        }
    }

    [Test]
    public void TheSlack_CoversThePositionsBetweenRecords() {
        var watch = new FallHeightWatch();
        Seen(watch, 0, 2500, 1000);
        watch.Fell(1500 + FallHeightWatch.Slack, At(1000), 200);
        Seen(watch, 200 + Grace, 1000);
        Assert.That(Seen(watch, 300 + Grace, 1000), Is.Empty);
    }

    // Each report starts a new window: an old top does not count for the next fall, but the records after the report do.
    [Test]
    public void AReport_StartsTheNextWindow() {
        var watch = new FallHeightWatch();
        Seen(watch, 0, 9000, 1000);
        watch.Fell(1500, At(1000), 200);
        Seen(watch, 300, 1200);
        Seen(watch, 200 + Grace, 1000);
        Seen(watch, 300 + Grace, 1000);

        watch.Fell(5000, At(1000), 5000);
        Seen(watch, 5000 + Grace, 1000);
        FallHeightWatch.Excess[] excess = Seen(watch, 5100 + Grace, 1000);

        Assert.That(excess, Has.Length.EqualTo(1));
        Assert.That(excess[0].Allowed, Is.EqualTo(200 + FallHeightWatch.Slack));
    }

    // Two reports before their records: the records after both count for each, so neither is found.
    [Test]
    public void TwoReportsBeforeTheirRecords_AreBothSettledByThem() {
        var watch = new FallHeightWatch();
        Seen(watch, 0, 1000);
        watch.Fell(3000, At(1000), 100);
        watch.Fell(2000, At(1000), 200);
        Seen(watch, 300, 4000, 1000);
        Seen(watch, 200 + Grace, 1000);
        Assert.That(Seen(watch, 300 + Grace, 1000), Is.Empty);
    }

    // A passenger's fall top may be a mid-air position no record of its own holds.
    [Test]
    public void AReportAfterAPassengerSeat_IsNotTested() {
        var watch = new FallHeightWatch();
        Seen(watch, 0, 1000);
        watch.Passenger();
        watch.Fell(5000, At(1000), 100);
        Seen(watch, 100 + Grace, 1000);
        Assert.That(Seen(watch, 200 + Grace, 1000), Is.Empty);

        watch.Fell(5000, At(1000), 5000);
        Seen(watch, 5000 + Grace, 1000);
        Assert.That(Seen(watch, 5100 + Grace, 1000), Has.Length.EqualTo(1), "the next report is tested again");
    }

    // A height or a position that is not a number is passed over.
    [Test]
    public void NotANumber_IsPassedOver() {
        var watch = new FallHeightWatch();
        watch.Fell(float.NaN, At(0), 0);
        Seen(watch, Grace, float.NaN);
        Assert.That(Seen(watch, Grace + 100, 0), Is.Empty);

        var noPosition = new FallHeightWatch();
        noPosition.Fell(5000, At(float.NaN), 0);
        Seen(noPosition, Grace, float.NaN);
        Assert.That(Seen(noPosition, Grace + 100, float.NaN), Is.Empty, "no z at all: nothing to measure by");
    }
}
