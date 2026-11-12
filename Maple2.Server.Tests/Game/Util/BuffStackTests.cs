using Maple2.Server.Game.Model;

namespace Maple2.Server.Tests.Game.Util;

// A stacked buff's values: status values and rates, dot and recovery amounts at 1 stack, 2 stacks and the maximum.
public class BuffStackTests {
    [Test]
    public void FactorIsTheCountAndAtLeastOne() {
        Assert.That(Buff.StackFactor(1), Is.EqualTo(1));
        Assert.That(Buff.StackFactor(2), Is.EqualTo(2));
        Assert.That(Buff.StackFactor(10), Is.EqualTo(10));
        // A buff without a stack maximum has count 0 and still counts once.
        Assert.That(Buff.StackFactor(0), Is.EqualTo(1));
    }

    // The counts a buff with the maximum reaches (Buff.AddStacks) and its status value at each.
    [TestCase(1, 5, 1, 12)]
    [TestCase(1, 5, 2, 24)]
    [TestCase(1, 5, 5, 60)]
    [TestCase(1, 5, 9, 60)] // more applications than the maximum stay at the maximum
    public void StatusValueByStacks(int first, int max, int applications, long expected) {
        int stacks = Buff.AddStacks(first, max);
        for (int i = 1; i < applications; i++) {
            stacks = System.Math.Min(max, stacks + Buff.AddStacks(1, max));
        }
        Assert.That(Buff.ByStacks(12L, stacks), Is.EqualTo(expected));
    }

    [TestCase(1, 0.03f)]
    [TestCase(2, 0.06f)]
    [TestCase(6, 0.18f)]
    public void StatusRateByStacks(int stacks, float expected) {
        Assert.That(Buff.ByStacks(0.03f, stacks), Is.EqualTo(expected).Within(1e-6f));
    }

    // A dot of 0.8 % of the target's health with a maximum of 3 stacks, on 83,258,250 health: KMS2 hits for
    // 666,066 at 1 stack and 1,998,198 at 3 (to the rounding of the health).
    [TestCase(1, 666_066)]
    [TestCase(2, 1_332_132)]
    [TestCase(3, 1_998_198)]
    public void DotAmountByStacks(int stacks, int expected) {
        long one = (long) (0.008 * 83_258_250);
        Assert.That(Buff.AmountByStacks(one, stacks), Is.EqualTo(expected));
    }

    [TestCase(1, 150)]
    [TestCase(2, 300)]
    [TestCase(4, 600)]
    public void RecoveryAmountByStacks(int stacks, int expected) {
        Assert.That(Buff.AmountByStacks(150, stacks), Is.EqualTo(expected));
    }

    [Test]
    public void AmountStaysInsideTheInt() {
        Assert.That(Buff.AmountByStacks(2_000_000_000, 3), Is.EqualTo(int.MaxValue));
    }
}
