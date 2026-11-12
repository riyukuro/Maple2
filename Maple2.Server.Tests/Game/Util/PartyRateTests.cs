using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

// The party buff level (members of the party in the same field) and the exp/meso rate of a kill.
public class PartyRateTests {
    [TestCase(0, 0)]
    [TestCase(1, 1)]
    [TestCase(2, 2)]
    [TestCase(3, 3)]
    [TestCase(9, 3)]
    public void LevelByOthersHere(int others, int level) {
        Assert.That(PartyBuff.Level(others), Is.EqualTo(level));
    }

    [Test]
    public void RateRaisesAmount() {
        Assert.Multiple(() => {
            Assert.That(KillRate.Apply(1000L, 0.05), Is.EqualTo(1050L));
            Assert.That(KillRate.Apply(200, 0.3), Is.EqualTo(260));
            Assert.That(KillRate.Apply(1000L, 0d), Is.EqualTo(1000L));
        });
    }

    [Test]
    public void RateNeverNegative() {
        Assert.That(KillRate.Apply(1000L, -2d), Is.EqualTo(0L));
    }

    [Test]
    public void IntRateCapped() {
        Assert.That(KillRate.Apply(int.MaxValue, 1d), Is.EqualTo(int.MaxValue));
    }
}
