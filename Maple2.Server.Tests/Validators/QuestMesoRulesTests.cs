using System.Collections.Generic;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;

namespace Maple2.Server.Tests.Validators;

// The meso of a quest's complete reward (FUN_1407622b0): the reward contents row of relativeMeso at the player's
// level, else the fixed money; the multicontrol questRewardMeso factor; the guild bonus of a relativeMeso. The rows
// have the shape of rewardcontent.xml 1003 (mesoTableID 2) and a static table; the amounts are made up.
public class QuestMesoRulesTests {
    private static RewardContentTable Table(float mesoFactor = 1f) {
        var rows = new Dictionary<int, RewardContentTable.Base> {
            [1003] = new(1003, MesoTableId: 2, ExpTableId: 0, MesoFactor: mesoFactor, ExpFactor: 0f, ItemTableId: 0, PrestigeExpTableId: 0),
            [1005] = new(1005, MesoTableId: 100001, ExpTableId: 0, MesoFactor: 1.5f, ExpFactor: 0f, ItemTableId: 0, PrestigeExpTableId: 0),
            [1006] = new(1006, MesoTableId: 9, ExpTableId: 0, MesoFactor: 1f, ExpFactor: 0f, ItemTableId: 0, PrestigeExpTableId: 0),
        };
        var levels = new Dictionary<int, Dictionary<int, long>> {
            [2] = new() { [1] = 1000, [50] = 4100, [60] = 5300 },
        };
        return new RewardContentTable(rows, new Dictionary<int, RewardContentTable.Item>(), new Dictionary<int, long> { [100001] = 700 },
            levels, new Dictionary<int, long>());
    }

    [Test]
    public void RewardMeso_IsTheLevelsValueTimesTheFactor() {
        Assert.That(Table().RewardMeso(1003, 50), Is.EqualTo(4100));
        Assert.That(Table(0.5f).RewardMeso(1003, 60), Is.EqualTo(2650));
        // A static table gives its one value at any level.
        Assert.That(Table().RewardMeso(1005, 1), Is.EqualTo(1050));
        Assert.That(Table().RewardMeso(1005, 77), Is.EqualTo(1050));
    }

    [Test]
    public void RewardMeso_WithoutRowTableOrLevel_IsZero() {
        Assert.That(Table().RewardMeso(1004, 50), Is.EqualTo(0)); // no row
        Assert.That(Table().RewardMeso(1006, 50), Is.EqualTo(0)); // no meso table
        Assert.That(Table().RewardMeso(1003, 0), Is.EqualTo(0));
        Assert.That(Table().RewardMeso(1003, 51), Is.EqualTo(0)); // a level the table lacks
    }

    [Test]
    public void Meso_RelativeReplacesTheFixedMoney() {
        Assert.That(QuestMesoRules.Meso(0, 4100, 1f, null), Is.EqualTo(4100));
        Assert.That(QuestMesoRules.Meso(500, 4100, 1f, null), Is.EqualTo(4100));
        Assert.That(QuestMesoRules.Meso(500, null, 1f, null), Is.EqualTo(500));
        Assert.That(QuestMesoRules.Meso(500, 0, 1f, null), Is.EqualTo(0));
    }

    // The guild npc level's userMeso factor adds factor - 1, only to a relativeMeso and only from 1 up.
    [Test]
    public void Meso_GuildBonusOnTheRelativeMeso() {
        Assert.That(QuestMesoRules.Meso(0, 4100, 1f, 1.2f), Is.EqualTo((long) (4100f * (1f + (1.2f - 1f)))));
        Assert.That(QuestMesoRules.Meso(0, 4100, 1f, 1f), Is.EqualTo(4100));
        Assert.That(QuestMesoRules.Meso(0, 4100, 1f, 0f), Is.EqualTo(4100)); // no quest npc
        Assert.That(QuestMesoRules.Meso(0, 4100, 1f, 0.5f), Is.EqualTo(4100));
        Assert.That(QuestMesoRules.Meso(500, null, 1f, 2f), Is.EqualTo(500));
    }

    // The quest type's factor scales the fixed money too, before the guild bonus.
    [Test]
    public void Meso_TheTypesFactorFirst() {
        Assert.That(QuestMesoRules.Meso(500, null, 3f, null), Is.EqualTo(1500));
        Assert.That(QuestMesoRules.Meso(0, 1000, 0.5f, 2f), Is.EqualTo(1000));
    }

    [Test]
    public void QuestRewardMesoFactor_FromMulticontrolRows() {
        (string, string, string)[] rows = [("questRewardMeso", "6", "2"), ("dungeonClearMeso", "6", "3"), ("questRewardMeso", "6", "9")];
        var constants = new ConstantsTable { QuestRewardMesoFactors = ConstantsTable.ReadMultiControl(rows, "questRewardMeso") };

        Assert.That(constants.QuestRewardMesoFactor(QuestType.GuildQuest), Is.EqualTo(2f));
        Assert.That(constants.QuestRewardMesoFactor(QuestType.EpicQuest), Is.EqualTo(1f));
        Assert.That(new ConstantsTable().QuestRewardMesoFactor(QuestType.GuildQuest), Is.EqualTo(1f));
    }
}
