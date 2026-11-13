using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

// The coded conditions of interact_npc (21230149) and dungeon_round_clear (21230154): the code is the npc and the
// dungeon room.
public class ContentCodeConditionTests {
    [TestCase(ConditionType.interact_npc, 21803502, true)]
    [TestCase(ConditionType.interact_npc, 21803510, true)]
    [TestCase(ConditionType.interact_npc, 21803504, false)]
    [TestCase(ConditionType.dungeon_round_clear, 24096001, true)]
    [TestCase(ConditionType.dungeon_round_clear, 24096002, false)]
    public void Code_IsOneOfTheListed(ConditionType type, long code, bool expected) {
        int[] codes = type switch {
            ConditionType.interact_npc => [21803500, 21803501, 21803502, 21803503, 21803510],
            _ => [24096001],
        };
        var condition = new ConditionMetadata(type, 1, new ConditionMetadata.Parameters(Integers: codes), null);
        Assert.That(condition.Check(null!, codeLong: code), Is.EqualTo(expected));
    }

    // 23500025 (create_blueprint) and 23100186 (banner) have no code: any raise counts.
    [TestCase(ConditionType.create_blueprint)]
    [TestCase(ConditionType.banner)]
    public void Uncoded_AnyRaiseCounts(ConditionType type) {
        var condition = new ConditionMetadata(type, 1, null, null);
        Assert.That(condition.Check(null!), Is.True);
    }
}
