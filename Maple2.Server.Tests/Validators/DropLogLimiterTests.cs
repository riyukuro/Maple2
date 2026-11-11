using Maple2.Server.Core.Network;

namespace Maple2.Server.Tests.Validators;

public class DropLogLimiterTests {
    [Test]
    public void FirstLinesThenOneCountLinePerInterval() {
        var limiter = new DropLogLimiter();
        var start = new System.DateTime(2024, 1, 1, 0, 0, 0, System.DateTimeKind.Utc);
        for (int i = 0; i < DropLogLimiter.FirstLines; i++) {
            Assert.That(limiter.ShouldLog(start, out int one), Is.True);
            Assert.That(one, Is.EqualTo(1));
        }

        // A flood inside the interval writes nothing.
        for (int i = 0; i < 1000; i++) {
            Assert.That(limiter.ShouldLog(start.AddSeconds(1), out _), Is.False);
        }

        // The next drop after the interval writes one line for all of them.
        Assert.That(limiter.ShouldLog(start + DropLogLimiter.Interval, out int count), Is.True);
        Assert.That(count, Is.EqualTo(1001));
        Assert.That(limiter.ShouldLog(start + DropLogLimiter.Interval, out _), Is.False);
        Assert.That(limiter.ShouldLog(start + DropLogLimiter.Interval + DropLogLimiter.Interval, out count), Is.True);
        Assert.That(count, Is.EqualTo(2));
    }
}
