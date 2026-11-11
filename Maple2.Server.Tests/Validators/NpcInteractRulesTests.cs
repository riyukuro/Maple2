using System.Numerics;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;

namespace Maple2.Server.Tests.Validators;

public class NpcInteractRulesTests {
    private static readonly NpcMetadataInteract Cannon = new(900701, 2, 4000, 8000);

    [Test]
    public void Parse_UseSkillGivesSkillLevelAndTimes() {
        NpcMetadataInteract? interact = NpcInteractRules.Parse("UseSkill,900701,2", 4000, 8000);

        Assert.That(interact, Is.EqualTo(Cannon));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("UseSkill,900701")]
    [TestCase("UseSkill,900701,0")]
    [TestCase("UseSkill,x,1")]
    [TestCase("OpenShop,900701,1")]
    public void Parse_NoOrUnknownFunctionIsNoInteract(string? function) {
        Assert.That(NpcInteractRules.Parse(function, 50, 0), Is.Null);
    }

    [Test]
    public void Parse_NegativeTimesAreZero() {
        Assert.That(NpcInteractRules.Parse("UseSkill,900702,1", -1, -5), Is.EqualTo(new NpcMetadataInteract(900702, 1, 0, 0)));
    }

    [Test]
    public void CanUse_Verdicts() {
        Vector3 npc = Vector3.Zero;
        var near = new Vector3(300, 0, 0);
        var far = new Vector3(300 + InteractRules.ReachSlack + 1, 0, 0);

        Assert.Multiple(() => {
            Assert.That(NpcInteractRules.CanUse(Cannon, false, near, npc, 300, 1000, 1000), Is.EqualTo(NpcInteractRules.Verdict.Ok));
            Assert.That(NpcInteractRules.CanUse(null, false, near, npc, 300, 0, 1000), Is.EqualTo(NpcInteractRules.Verdict.NoInteract));
            Assert.That(NpcInteractRules.CanUse(Cannon, true, near, npc, 300, 0, 1000), Is.EqualTo(NpcInteractRules.Verdict.Dead));
            Assert.That(NpcInteractRules.CanUse(Cannon, false, far, npc, 300, 0, 1000), Is.EqualTo(NpcInteractRules.Verdict.OutOfReach));
            Assert.That(NpcInteractRules.CanUse(Cannon, false, near, npc, 300, 1001, 1000), Is.EqualTo(NpcInteractRules.Verdict.CoolingDown));
        });
    }

    [Test]
    public void Talkable_WithInteractOutsideCoolTime() {
        Assert.Multiple(() => {
            Assert.That(NpcInteractRules.Talkable(Cannon, 0, 1000), Is.True);
            Assert.That(NpcInteractRules.Talkable(Cannon, 1001, 1000), Is.False);
            Assert.That(NpcInteractRules.Talkable(null, 0, 1000), Is.False);
        });
    }
}
