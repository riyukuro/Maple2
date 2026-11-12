using System;
using Maple2.Model.Enum;
using Maple2.Server.Game.Util;
using WorldRules = Maple2.Server.World.TimeEventRules;

namespace Maple2.Server.Tests.Game.Util;

public class TimeEventRulesTests {
    private static readonly DateTime Start = new(2020, 1, 1, 0, 5, 0);
    private static readonly DateTime End = new(2030, 1, 1);
    private static readonly TimeSpan Hour = TimeSpan.FromHours(1);

    // A probability of 30 runs for 30 of the 100 rolls (the portal rows ran for 31, a row of 0 for one).
    [TestCase(100, 99, true)]
    [TestCase(30, 29, true)]
    [TestCase(30, 30, false)]
    [TestCase(0, 0, false)]
    public void Runs_ByTheRowsProbability(int probability, int roll, bool expected) {
        Assert.That(WorldRules.Runs(probability, roll), Is.EqualTo(expected));
    }

    [Test]
    public void FirstRun_IsTheNextCycleStep() {
        var now = new DateTime(2026, 11, 12, 10, 20, 0);
        Assert.Multiple(() => {
            Assert.That(WorldRules.FirstRun(Start, Hour, End, now), Is.EqualTo(new DateTime(2026, 11, 12, 11, 5, 0)));
            Assert.That(WorldRules.FirstRun(Start, Hour, End, new DateTime(2026, 11, 12, 10, 5, 0)), Is.EqualTo(new DateTime(2026, 11, 12, 10, 5, 0)));
            Assert.That(WorldRules.FirstRun(Start, TimeSpan.FromHours(2), End, now), Is.EqualTo(new DateTime(2026, 11, 12, 12, 5, 0)));
        });
    }

    // A row whose StartTime is still ahead runs at it (a portal row like that was never scheduled).
    [Test]
    public void FirstRun_OfARowNotStartedYetIsItsStartTime() {
        var start = new DateTime(2027, 1, 1, 0, 35, 0);
        Assert.That(WorldRules.FirstRun(start, Hour, End, new DateTime(2026, 11, 12)), Is.EqualTo(start));
    }

    [Test]
    public void FirstRun_NoneWithoutACycleOrAfterTheEnd() {
        var now = new DateTime(2026, 11, 12, 10, 20, 0);
        Assert.Multiple(() => {
            Assert.That(WorldRules.FirstRun(Start, TimeSpan.Zero, End, now), Is.Null);
            Assert.That(WorldRules.FirstRun(Start, Hour, new DateTime(2026, 11, 12, 11, 0, 0), now), Is.Null);
            Assert.That(WorldRules.FirstRun(Start, Hour, new DateTime(2025, 1, 1), now), Is.Null);
        });
    }

    [TestCase(MapType.None, true)]
    [TestCase(MapType.Telescope, true)]
    [TestCase(MapType.Alikar, true)]
    [TestCase(MapType.Shelter, true)]
    public void PortalAnnounce_InFieldMaps(MapType type, bool expected) {
        Assert.That(TimeEventRules.PortalAnnouncedIn(type), Is.EqualTo(expected));
    }

    [Test]
    public void PortalAnnounce_NotBetweenTheTwoRanges() {
        Assert.That(TimeEventRules.PortalAnnouncedIn(MapType.Telescope + 1), Is.False);
        Assert.That(TimeEventRules.PortalAnnouncedIn(MapType.Shelter + 1), Is.False);
    }

    // The idle threshold is four minutes: a boss hit three minutes ago stays, one hit five minutes ago or never leaves.
    [TestCase(0, 1_000_000, true)]
    [TestCase(1_000_000 - 180_000, 1_000_000, false)]
    [TestCase(1_000_000 - 240_000, 1_000_000, true)]
    [TestCase(1_000_000 - 300_000, 1_000_000, true)]
    public void BossLeaves_WhenNotHitWithinTheThreshold(long lastDamageTick, long nowTick, bool expected) {
        Assert.That(TimeEventRules.BossLeaves(lastDamageTick, nowTick, TimeSpan.FromMinutes(4)), Is.EqualTo(expected));
    }
}
