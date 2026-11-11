using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

// Revival mode 1, the meret revive in place: the price the client shows and the debuff limit.
public class RevivalRulesTests {
    [Test]
    public void Cost_NoReduction_IsTheLuaPrice() {
        Assert.That(RevivalRules.MeretCost(30, 0.5f, 0f), Is.EqualTo(30));
        Assert.That(RevivalRules.MeretCost(30, 0f, 0.4f), Is.EqualTo(30));
    }

    [Test]
    public void Cost_ReductionIsCappedAndTruncated() {
        Assert.That(RevivalRules.MeretCost(30, 0.5f, 0.25f), Is.EqualTo(22));
        Assert.That(RevivalRules.MeretCost(30, 0.5f, 0.9f), Is.EqualTo(15));
        Assert.That(RevivalRules.MeretCost(20, 0.5f, 0.5f), Is.EqualTo(10));
    }

    [Test]
    public void Cost_NeverBelowZero() {
        Assert.That(RevivalRules.MeretCost(30, 2f, 1.5f), Is.EqualTo(0));
    }

    [Test]
    public void Debuff_AtTopStack_Closes() {
        Assert.That(RevivalRules.DebuffAllows(90000001, 2, 3, false), Is.True);
        Assert.That(RevivalRules.DebuffAllows(90000001, 3, 3, false), Is.False);
        Assert.That(RevivalRules.DebuffAllows(90000001, 3, 3, true), Is.True);
        Assert.That(RevivalRules.DebuffAllows(0, 3, 3, false), Is.True);
    }
}
