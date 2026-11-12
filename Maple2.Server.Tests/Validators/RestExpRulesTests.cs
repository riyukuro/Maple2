using Maple2.Model.Validators;

namespace Maple2.Server.Tests.Validators;

public class RestExpRulesTests {
    // A kill with enough rest gets 3x at rate 20000 and spends all of it; short rest gives the plain gain and empties it.
    [TestCase(100, 1000, 20000, 300, 700)]
    [TestCase(100, 300, 20000, 300, 0)]
    [TestCase(100, 299, 20000, 100, 199)]
    [TestCase(100, 50, 20000, 100, 0)]
    [TestCase(100, 0, 20000, 100, 0)]
    [TestCase(100, 1000, 0, 100, 900)]
    public void Kill(long gain, long rest, int rate, long expectedGain, long expectedRest) {
        Assert.That(RestExpRules.Spend(gain, rest, kill: true, rate), Is.EqualTo((expectedGain, expectedRest)));
    }

    // Other sources: no bonus, the rest still drops by the gain.
    [TestCase(100, 1000, 100, 900)]
    [TestCase(100, 40, 100, 0)]
    [TestCase(0, 40, 0, 40)]
    public void OtherSource(long gain, long rest, long expectedGain, long expectedRest) {
        Assert.That(RestExpRules.Spend(gain, rest, kill: false, 20000), Is.EqualTo((expectedGain, expectedRest)));
    }

    // A tick adds floor(base x 35001 / 10000000), at least 1, up to NextExp x the max rate / 10000.
    [TestCase(0, 400000, 1000, 100000, 1400)]
    [TestCase(0, 100, 1000, 100000, 1)]
    [TestCase(5, 0, 1000, 100000, 6)]
    [TestCase(9999, 400000, 1000, 100000, 10000)]
    [TestCase(10000, 400000, 1000, 100000, 10000)]
    [TestCase(12000, 400000, 1000, 100000, 12000)]
    [TestCase(0, 400000, 1000, 0, 0)]
    public void Tick(long rest, long expBase, long nextExp, int maxRate, long expected) {
        Assert.That(RestExpRules.Tick(rest, expBase, nextExp, maxRate), Is.EqualTo(expected));
    }
}
