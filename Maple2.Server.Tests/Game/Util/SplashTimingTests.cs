using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

public class SplashTimingTests {
    [Test]
    public void DelayOnlySplashFiresAfterItsDelay() {
        // 40213001's splash 40213002: delay 1500, interval 0, fireCount 1.
        Assert.That(SplashTiming.FirstFire(false, false, 1500, 0), Is.EqualTo(1500));
    }

    [Test]
    public void DelayAndIntervalAdd() {
        Assert.That(SplashTiming.FirstFire(false, false, 500, 300), Is.EqualTo(800));
        Assert.That(SplashTiming.FirstFire(false, false, 1000, 500), Is.EqualTo(1500));
        Assert.That(SplashTiming.FirstFire(false, false, 0, 1000), Is.EqualTo(1000));
        Assert.That(SplashTiming.FirstFire(false, false, 0, 0), Is.EqualTo(0));
    }

    [Test]
    public void ImmediateSplashIgnoresDelay() {
        Assert.That(SplashTiming.FirstFire(true, false, 0, 1000), Is.EqualTo(0));
        Assert.That(SplashTiming.FirstFire(true, false, 50, 500), Is.EqualTo(0));
    }

    [Test]
    public void SensingSplashKeepsItsInterval() {
        Assert.That(SplashTiming.FirstFire(false, true, 10000, 0), Is.EqualTo(0));
        Assert.That(SplashTiming.FirstFire(false, true, 0, 10000), Is.EqualTo(10000));
    }
}
