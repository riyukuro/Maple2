using Maple2.Model.Validators;

namespace Maple2.Server.Tests.Validators;

public class NpcTargetRulesTests {
    // A dead actor is not a target, so a monster drops it and does not pick it again before the revive.
    [TestCase(false, false, true)]
    [TestCase(true, false, false)]
    [TestCase(false, true, false)]
    [TestCase(true, true, false)]
    public void CanTarget_OnlyTheLivingAndVisible(bool dead, bool invisible, bool expected) {
        Assert.That(NpcTargetRules.CanTarget(dead, invisible), Is.EqualTo(expected));
    }

    // isKeepBattle holds a target that is out of sight, never one that is gone (dead or off the field).
    [TestCase(false, false, false, false)]
    [TestCase(false, false, true, false)]
    [TestCase(false, true, false, true)]
    [TestCase(false, true, true, false)]
    [TestCase(true, false, false, true)]
    [TestCase(true, false, true, true)]
    [TestCase(true, true, true, true)]
    public void LosesTarget_KeepBattleDoesNotHoldAGoneTarget(bool gone, bool outOfSight, bool keepBattle, bool expected) {
        Assert.That(NpcTargetRules.LosesTarget(gone, outOfSight, keepBattle), Is.EqualTo(expected));
    }

    // The walk to a target stops when the npc drops that target (0) or takes another one.
    [TestCase(101, 101, false)]
    [TestCase(101, 0, true)]
    [TestCase(101, 102, true)]
    public void ChaseEnds_WhenTheChasedActorIsNoLongerTheTarget(int chased, int target, bool expected) {
        Assert.That(NpcTargetRules.ChaseEnds(chased, target), Is.EqualTo(expected));
    }
}
