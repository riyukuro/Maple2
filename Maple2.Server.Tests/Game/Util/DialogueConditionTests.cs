using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

// dialogue: the client files it under the same condition kind as talk_in (0x27). The code is the npc, the target the
// npc script the talk reaches (40001290: code 11001008, target 10, that npc's random-talk script 10).
public class DialogueConditionTests {
    private const int Npc = 11001008;

    private static ConditionMetadata Dialogue(ConditionMetadata.Parameters? target) =>
        new(ConditionType.dialogue, 1, new ConditionMetadata.Parameters(Integers: [Npc]), target);

    [TestCase(10, Npc, true)]
    [TestCase(0, Npc, false)]
    [TestCase(11, Npc, false)]
    [TestCase(10, Npc + 1, false)]
    public void Target_IsTheScriptTheTalkReaches(long script, long npc, bool expected) {
        ConditionMetadata condition = Dialogue(new ConditionMetadata.Parameters(Integers: [10]));
        Assert.That(condition.Check(null!, targetLong: script, codeLong: npc), Is.EqualTo(expected));
    }

    // Without a target the talk's start counts, not each script after it.
    [TestCase(0, true)]
    [TestCase(10, false)]
    public void NoTarget_CountsTheTalkStartOnly(long script, bool expected) {
        Assert.Multiple(() => {
            Assert.That(Dialogue(null).Check(null!, targetLong: script, codeLong: Npc), Is.EqualTo(expected));
            Assert.That(Dialogue(new ConditionMetadata.Parameters(Strings: [""])).Check(null!, targetLong: script, codeLong: Npc), Is.EqualTo(expected));
        });
    }

    // trigger: the target is the map the trigger runs in (jordysave: code "jordysave", target 52000119).
    [TestCase(52000119, true)]
    [TestCase(52000120, false)]
    public void Trigger_TargetIsTheMap(long map, bool expected) {
        var condition = new ConditionMetadata(ConditionType.trigger, 1,
            new ConditionMetadata.Parameters(Strings: ["jordysave"]), new ConditionMetadata.Parameters(Integers: [52000119]));
        Assert.That(condition.Check(null!, targetLong: map, codeString: "jordysave"), Is.EqualTo(expected));
    }

    // A trigger row without a target counts on any map.
    [Test]
    public void Trigger_NoTarget_AnyMap() {
        var condition = new ConditionMetadata(ConditionType.trigger, 1, new ConditionMetadata.Parameters(Strings: ["jordysave"]), null);
        Assert.That(condition.Check(null!, targetLong: 2000001, codeString: "jordysave"), Is.True);
    }
}
