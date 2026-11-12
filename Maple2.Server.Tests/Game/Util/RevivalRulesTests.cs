using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

// Revival mode 1, the meret revive in place: the price the client shows and the debuff limit.
public class RevivalRulesTests {
    [TestCase(true, 0, 2000000, true)]          // a revive in place
    [TestCase(false, 0, 2000000, true)]         // a safe revive at the map's spawn point
    [TestCase(false, 2000000, 2000000, true)]   // revivalReturnId is the map itself
    [TestCase(false, 2000001, 2000000, false)]  // sent to another map: its field entry gives the buffs
    public void RevivalRestoresLastingBuffs(bool instant, int returnId, int mapId, bool restores) {
        Assert.That(RevivalRules.RestoresLastingBuffs(instant, returnId, mapId), Is.EqualTo(restores));
    }

    [TestCase(true, 0, true)]
    [TestCase(false, 0, true)]
    [TestCase(false, 1, false)]
    [TestCase(false, 2759, false)]
    public void FieldEntryFillsHpOfADeadPlayer(bool leftDead, long hp, bool fills) {
        Assert.That(RevivalRules.FillsHpOnFieldEntry(leftDead, hp), Is.EqualTo(fills));
    }

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
    [Test]
    public void DisableRevivalBuffBlocksReviveInPlace() {
        Assert.That(RevivalRules.BuffBlocks([]), Is.False);
        Assert.That(RevivalRules.BuffBlocks([false, false]), Is.False);
        Assert.That(RevivalRules.BuffBlocks([false, true]), Is.True);
    }
}
