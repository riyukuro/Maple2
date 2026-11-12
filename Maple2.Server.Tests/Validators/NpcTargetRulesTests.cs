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
}
