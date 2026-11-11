using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

public class SkinTabPeriodTests {
    private const long Now = 1_700_000_000;
    private const long Day = SkinTabPeriod.SecondsPerDay;

    [Test]
    public void GetState_FollowsStartAndEnd() {
        Assert.That(SkinTabPeriod.GetState(0, 0, Now), Is.EqualTo(SkinTabPeriod.State.NotStarted));
        Assert.That(SkinTabPeriod.GetState(Now - Day, 0, Now), Is.EqualTo(SkinTabPeriod.State.Limitless));
        Assert.That(SkinTabPeriod.GetState(Now - Day, Now + 1, Now), Is.EqualTo(SkinTabPeriod.State.Active));
        Assert.That(SkinTabPeriod.GetState(Now - Day, Now, Now), Is.EqualTo(SkinTabPeriod.State.Expired));
        Assert.That(SkinTabPeriod.IsActive(0, 0, Now), Is.False);
        Assert.That(SkinTabPeriod.IsActive(Now - Day, 0, Now), Is.True);
        Assert.That(SkinTabPeriod.IsActive(Now - Day, Now, Now), Is.False);
    }

    [Test]
    public void TryActivate_StartsANewPeriod() {
        Assert.That(SkinTabPeriod.TryActivate(0, 0, Now, 30, 999, out long start, out long end), Is.True);
        Assert.That(start, Is.EqualTo(Now));
        Assert.That(end, Is.EqualTo(Now + 30 * Day));

        // An expired period starts again from now.
        Assert.That(SkinTabPeriod.TryActivate(Now - 40 * Day, Now - 10 * Day, Now, 0, 999, out start, out end), Is.True);
        Assert.That(start, Is.EqualTo(Now));
        Assert.That(end, Is.EqualTo(0));
    }

    [Test]
    public void TryActivate_ExtendsAnActivePeriod() {
        Assert.That(SkinTabPeriod.TryActivate(Now - Day, Now + Day, Now, 90, 999, out long start, out long end), Is.True);
        Assert.That(start, Is.EqualTo(Now - Day));
        Assert.That(end, Is.EqualTo(Now + 91 * Day));

        // The no-end item on an active period removes the end.
        Assert.That(SkinTabPeriod.TryActivate(Now - Day, Now + Day, Now, 0, 999, out start, out end), Is.True);
        Assert.That(start, Is.EqualTo(Now - Day));
        Assert.That(end, Is.EqualTo(0));
    }

    [Test]
    public void TryActivate_RefusesWhatTheClientRefuses() {
        // Already without an end.
        Assert.That(SkinTabPeriod.TryActivate(Now - Day, 0, Now, 30, 999, out long start, out long end), Is.False);
        Assert.That((start, end), Is.EqualTo((Now - Day, 0L)));

        // Exactly the maximum span passes, one second more does not.
        Assert.That(SkinTabPeriod.TryActivate(Now, Now + 10 * Day, Now, 20, 30, out _, out end), Is.True);
        Assert.That(end, Is.EqualTo(Now + 30 * Day));
        Assert.That(SkinTabPeriod.TryActivate(Now, Now + 10 * Day + 1, Now, 20, 30, out _, out _), Is.False);
        Assert.That(SkinTabPeriod.TryActivate(Now, Now + 11 * Day, Now, 20, 30, out _, out _), Is.False);

        Assert.That(SkinTabPeriod.TryActivate(0, 0, Now, -1, 999, out _, out _), Is.False);
    }
}
