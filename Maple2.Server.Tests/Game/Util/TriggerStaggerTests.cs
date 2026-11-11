using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

// set_effect start_delay / interval: id i of the list changes at start_delay + i * interval.
public class TriggerStaggerTests {
    // 52000150: four rows of five effects, start_delay 0/200/400/600, interval 200.
    [TestCase(0, 0, 200, 0)]
    [TestCase(4, 0, 200, 800)]
    [TestCase(0, 200, 200, 200)]
    [TestCase(3, 600, 200, 1200)]
    // 52000151: chains lit 100 ms apart.
    [TestCase(11, 0, 100, 1100)]
    // The other placed actions give 0 for both: every id changes at once.
    [TestCase(0, 0, 0, 0)]
    [TestCase(7, 0, 0, 0)]
    public void Delay(int index, int startDelay, int interval, int expected) {
        Assert.That(TriggerStagger.Delay(index, startDelay, interval), Is.EqualTo(expected));
    }

    [Test]
    public void NegativeAndHugeValues() {
        Assert.Multiple(() => {
            Assert.That(TriggerStagger.Delay(3, -100, -50), Is.EqualTo(0));
            Assert.That(TriggerStagger.Delay(1000, 0, int.MaxValue), Is.EqualTo(int.MaxValue));
        });
    }

    // The 52000150 rows start one interval apart, so together they make one diagonal wave: row r, column c at 200 * (r + c).
    [Test]
    public void DiagonalWave() {
        for (int row = 0; row < 4; row++) {
            for (int col = 0; col < 5; col++) {
                Assert.That(TriggerStagger.Delay(col, row * 200, 200), Is.EqualTo(200 * (row + col)));
            }
        }
    }
}
