using System.Numerics;
using Maple2.Model.Enum;
using Maple2.Server.Game.Util;
using static Maple2.Model.Metadata.AiMetadata;

namespace Maple2.Server.Tests.Game.Util;

public class AiNodeProbTests {
    // A Random whose Next(max) returns a fixed roll.
    private class FixedRoll(int roll) : System.Random {
        public override int Next(int maxValue) => roll;
    }

    // KR AI_TurkaReaperCostume_SummonTypeA.xml: <node name="buff" type="add" id="50003306" prob="60" />, commented
    // as a random reflect buff with a high chance: it runs on rolls 0-59 of 100 and not on 60-99.
    [Test]
    public void Buff_Prob60_RunsOnSixtyRollsInAHundred() {
        var buff = new BuffNode("buff", [], 50003306, NodeBuffType.Add, 1, 60, 0, 0, false, false);
        int prob = AiNodeProb.Of(buff);

        Assert.That(prob, Is.EqualTo(60));
        Assert.That(AiNodeProb.Runs(prob, new FixedRoll(59)), Is.True);
        Assert.That(AiNodeProb.Runs(prob, new FixedRoll(60)), Is.False);
    }

    // KR AI_Mason_FR_Lv1.xml: <node name="teleport" pos="-1981, 1500, 1951" prob="25"> holding
    // <node name="skill" prob="40" idx="10">: the teleport runs on a quarter of the rolls, the skill under it on 40 in 100.
    [Test]
    public void TeleportAndSkill_TakeTheirOwnProb() {
        var skill = new SkillNode("skill", [], 10, 1, 40, false, Vector3.Zero, 1, 0, 0, 0, 0, false);
        var teleport = new TeleportNode("teleport", [skill], new Vector3(-1981, 1500, 1951), 25, Vector3.Zero, 0, 0, 0, false);

        Assert.That(AiNodeProb.Of(teleport), Is.EqualTo(25));
        Assert.That(AiNodeProb.Of(skill), Is.EqualTo(40));
        Assert.That(AiNodeProb.Runs(25, new FixedRoll(24)), Is.True);
        Assert.That(AiNodeProb.Runs(25, new FixedRoll(25)), Is.False);
    }

    // A node without prob gets 100 from the ingest (75644 KR values): it always runs, whatever the roll.
    [Test]
    public void Prob100_AlwaysRuns() {
        var standby = new StandbyNode("standby", [], 1000, 100, "", Vector3.Zero, 1, 0, 0, false);

        Assert.That(AiNodeProb.Of(standby), Is.EqualTo(100));
        Assert.That(AiNodeProb.Runs(100, new FixedRoll(99)), Is.True);
    }

    // The select's prob is the weight list of its children (AiSelect), not a chance for the select itself.
    [Test]
    public void Select_IsNotRolled() {
        var select = new SelectNode("select", [], [100, 0], false);

        Assert.That(AiNodeProb.Of(select), Is.EqualTo(100));
    }
}
