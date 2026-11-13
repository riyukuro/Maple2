using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

// ShutdownNotice 0xAF: KR only, the time a channel keeps, its send at entry, and the countdown notices left.
public class ShutdownRulesTests {
    [TestCase("KR", true)]
    [TestCase("NA", false)]
    [TestCase("CN", false)]
    public void Sent_KrOnly(string locale, bool sent) {
        Assert.That(ShutdownRules.Sent(locale), Is.EqualTo(sent));
    }

    [TestCase(0L, 0, 5000L, 5000L)]  // time: kept
    [TestCase(5000L, 0, 0L, 0L)]     // cancel: cleared
    [TestCase(5000L, 1, 0L, 5000L)]  // quit
    [TestCase(5000L, 2, 0L, 5000L)]  // maintenance
    [TestCase(5000L, 3, 0L, 5000L)]  // notice
    public void Kept_OnlyKindZeroChangesTheTime(long kept, int kind, long time, long expected) {
        Assert.That(ShutdownRules.Kept(kept, kind, time), Is.EqualTo(expected));
    }

    [TestCase("KR", 2000L, 1000L, true)]
    [TestCase("KR", 1000L, 1000L, false)] // reached
    [TestCase("KR", 500L, 1000L, false)]  // past
    [TestCase("KR", 0L, 1000L, false)]    // none, or cancelled
    [TestCase("NA", 2000L, 1000L, false)]
    public void SendAtEntry_WhileAheadUnderKr(string locale, long kept, long now, bool sent) {
        Assert.That(ShutdownRules.SendAtEntry(locale, kept, now), Is.EqualTo(sent));
    }

    [TestCase(4, new int[0])]
    [TestCase(5, new[] { 5 })]
    [TestCase(10, new[] { 5 })]
    [TestCase(15, new[] { 15, 5 })]
    [TestCase(60, new[] { 30, 15, 5 })]
    public void NoticesLeft(int minutes, int[] notices) {
        Assert.That(ShutdownRules.NoticesLeft(minutes), Is.EqualTo(notices));
    }
}
