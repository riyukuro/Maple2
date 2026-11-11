using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

public class DebugButtonRulesTests {
    [TestCase(1, 1, true)]
    [TestCase(6, 6, true)]
    [TestCase(3, 5, true)]
    [TestCase(0, 3, false)]
    [TestCase(3, 7, false)]
    [TestCase(-1, 2, false)]
    public void DiceMustBeOneToSix(int dice1, int dice2, bool expected) {
        Assert.That(DebugButtonRules.DiceAllowed(dice1, dice2), Is.EqualTo(expected));
    }

    [Test]
    public void RollMovesBySumAndADoubleGivesAFreeRoll() {
        Assert.That(DebugButtonRules.AfterRoll(10, 0, 2, 5), Is.EqualTo((17, 0)));
        Assert.That(DebugButtonRules.AfterRoll(10, 1, 4, 4), Is.EqualTo((18, 2)));
    }

    [TestCase((byte) 0, 200L, 200L)]
    [TestCase((byte) 2, 200L, 60L)]
    [TestCase((byte) 3, 200L, 198L)]
    [TestCase((byte) 3, 1L, 0L)]
    public void UpButtonsSetTheCounter(byte button, long goal, long expected) {
        Assert.That(DebugButtonRules.TryUpCounter(button, goal, out long counter), Is.True);
        Assert.That(counter, Is.EqualTo(expected));
    }

    [TestCase((byte) 1, 200L)]
    [TestCase((byte) 4, 200L)]
    [TestCase((byte) 0, 0L)]
    public void DownAndUnknownButtonsAreNotUpButtons(byte button, long goal) {
        Assert.That(DebugButtonRules.TryUpCounter(button, goal, out long _), Is.False);
    }

    [Test]
    public void FailChargesFillToAFullRateOrEmpty() {
        Assert.That(DebugButtonRules.FailCharges(true, 100, 1), Is.EqualTo(100));
        Assert.That(DebugButtonRules.FailCharges(true, 100, 3), Is.EqualTo(34));
        Assert.That(DebugButtonRules.FailCharges(false, 100, 1), Is.EqualTo(0));
        Assert.That(DebugButtonRules.FailCharges(true, 100, 0), Is.EqualTo(0));
    }

    [TestCase((byte) 0, true)]
    [TestCase((byte) 3, true)]
    [TestCase((byte) 4, false)]
    [TestCase((byte) 255, false)]
    public void OnlyTheFourAchieveButtonsAreKnown(byte button, bool expected) {
        Assert.That(DebugButtonRules.KnownAchieveButton(button), Is.EqualTo(expected));
    }

    [TestCase(0, true)]
    [TestCase(14, true)]
    [TestCase(15, false)]
    [TestCase(-1, false)]
    public void ForceEnchantStopsAtTheLastStep(int enchants, bool expected) {
        Assert.That(DebugButtonRules.ForceEnchantAllowed(enchants), Is.EqualTo(expected));
    }

    // requested 0 = top up to the need, a number = that many; never more than 5 stacks
    [TestCase(0, 20, 5, 100, 15)]
    [TestCase(0, 20, 20, 100, 0)]
    [TestCase(0, 20, 50, 100, 0)]
    [TestCase(10, 20, 50, 100, 10)]
    [TestCase(10, 0, 0, 100, 0)]
    [TestCase(-1, 20, 0, 100, 0)]
    [TestCase(int.MaxValue, 20, 0, 100, 500)]
    [TestCase(501, 20, 0, 100, 500)]
    [TestCase(500, 20, 0, 100, 500)]
    [TestCase(int.MaxValue, 20, 0, 0, 5)]
    [TestCase(int.MaxValue, 20, 0, int.MaxValue, int.MaxValue)]
    [TestCase(0, 900, 0, 100, 500)]
    public void FillAmountOfAnIngredient(int requested, int need, int held, int slotMax, int expected) {
        Assert.That(DebugButtonRules.FillAmount(requested, need, held, slotMax), Is.EqualTo(expected));
    }
}
