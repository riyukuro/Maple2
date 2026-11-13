using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

// talk_in: the code is the npc, the target the npc script the talk reaches (22200019 "Deliver item 30000181 to npc
// 11000327": code 11000327, target 41, the script whose function collects the item). The talk's start counts with
// target 0, each npc script it enters with that script's id.
public class TalkInConditionTests {
    private const int Npc = 11000327;

    private static ConditionMetadata TalkIn(ConditionMetadata.Parameters? target) =>
        new(ConditionType.talk_in, 1, new ConditionMetadata.Parameters(Integers: [Npc]), target);

    [TestCase(41, Npc, true)]
    [TestCase(40, Npc, false)]
    [TestCase(0, Npc, false)]
    [TestCase(41, Npc + 1, false)]
    public void Target_IsTheScriptTheTalkReaches(long script, long npc, bool expected) {
        ConditionMetadata condition = TalkIn(new ConditionMetadata.Parameters(Integers: [41]));
        Assert.That(condition.Check(null!, targetLong: script, codeLong: npc), Is.EqualTo(expected));
    }

    // A row without a target, or with target="" (parsed as one empty string), counts the talk's start only, not each
    // script the talk enters after it.
    [TestCase(0, true)]
    [TestCase(10, false)]
    public void NoTarget_CountsTheTalkStartOnly(long script, bool expected) {
        Assert.Multiple(() => {
            Assert.That(TalkIn(null).Check(null!, targetLong: script, codeLong: Npc), Is.EqualTo(expected));
            Assert.That(TalkIn(new ConditionMetadata.Parameters(Strings: [""])).Check(null!, targetLong: script, codeLong: Npc), Is.EqualTo(expected));
        });
    }
}
