using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Validators;

public class MiniGameClearRulesTests {
    [TestCase("UserOpenMiniGameExtraReward", true)]
    [TestCase("miniGame", false)]
    [TestCase("", false)]
    public void Reward_CountsForTheUserOpenContent(string contentType, bool expected) {
        Assert.That(MiniGameClearRules.CountsAsUserOpenClear(contentType), Is.EqualTo(expected));
    }

    // The row lists the three mini-game fields; a reward on another field does not count.
    [TestCase(81000001, true)]
    [TestCase(81000003, true)]
    [TestCase(81000004, false)]
    public void Row_CountsItsFields(int mapId, bool expected) {
        var condition = new ConditionMetadata(ConditionType.useropen_minigame_clear, 1,
            new ConditionMetadata.Parameters(Integers: [81000001, 81000002, 81000003]), null);
        Assert.That(condition.Check(null!, codeLong: mapId), Is.EqualTo(expected));
    }
}
