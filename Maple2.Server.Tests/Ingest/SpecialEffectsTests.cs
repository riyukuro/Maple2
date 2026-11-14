using System.Collections.Generic;
using Maple2.File.Ingest.Helpers;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;

namespace Maple2.Server.Tests.Ingest;

// The SpecialEffectProperty of the KR additional effect rows (type, probability, values unescaped, activateMsg).
public class SpecialEffectsTests {
    private static SpecialEffectMetadata Row(string type, string probability, string values, string message = "") {
        SpecialEffectMetadata? effect = SpecialEffects.Read(type, probability, values, message);
        Assert.That(effect, Is.Not.Null);
        return effect!;
    }

    [Test]
    public void Fishing_110001027And110000101() {
        SpecialEffectMetadata big = Row("9", "", "<v onlyBigSize=\"1\" improveMasteryRate=\"1\" improveExpRate=\"1\" />", "s_additionalEffect_Special_110001027");
        Assert.That((big.Type, big.Probability, big.OnlyBigSize, big.MasteryRate, big.ExpRate, big.ActivateMessage),
            Is.EqualTo((9, 1f, true, 1f, 1f, "s_additionalEffect_Special_110001027")));
        SpecialEffectMetadata small = Row("9", "", "<v onlyBigSize=\"0\" improveMasteryRate=\"0\" improveExpRate=\"0.04\" />");
        Assert.That((small.OnlyBigSize, small.MasteryRate, small.ExpRate, small.ActivateMessage), Is.EqualTo((false, 0f, 0.04f, "")));
    }

    [Test]
    public void Music_110001025And110000105() {
        SpecialEffectMetadata chance = Row("10", "0.05", "<v improveExpRate=\"2\" />", "s_additionalEffect_Special_110001025");
        Assert.That((chance.Type, chance.Probability, chance.ExpRate), Is.EqualTo((10, 0.05f, 2f)));
        Assert.That(Row("10", "", "<v improveExpRate=\"0.04\" />").Probability, Is.EqualTo(1f), "absent = 1");
    }

    [Test]
    public void QuestReward_110001021To110001023() {
        SpecialEffectMetadata exp = Row("7", "0.05", "<v repeatType=\"2\" improveMesoRate=\"0.0\" improveExpRate=\"4\" improveLuRate=\"0\" />");
        SpecialEffectMetadata meso = Row("7", "0.05", "<v repeatType=\"2\" improveMesoRate=\"4.0\" improveExpRate=\"0\" improveLuRate=\"0\" />");
        SpecialEffectMetadata rue = Row("7", "0.05", "<v repeatType=\"2\" improveMesoRate=\"0.0\" improveExpRate=\"0\" improveLuRate=\"4\" />");
        Assert.That((exp.RepeatType, exp.ExpRate, exp.MesoRate, exp.RueRate), Is.EqualTo((2, 4f, 0f, 0f)));
        Assert.That((meso.ExpRate, meso.MesoRate, meso.RueRate), Is.EqualTo((0f, 4f, 0f)));
        Assert.That((rue.ExpRate, rue.MesoRate, rue.RueRate), Is.EqualTo((0f, 0f, 4f)));
    }

    [Test]
    public void ChestDropBox_110001019And110001020() {
        SpecialEffectMetadata gold = Row("13", "0.002", "<v expType=\"4\" globalDropBoxId=\"\" individualDropBoxId=\"382000001\" />");
        SpecialEffectMetadata wood = Row("13", "0.006", "<v expType=\"6\" globalDropBoxId=\"\" individualDropBoxId=\"382000002\" />");
        Assert.That((gold.ExpType, gold.GlobalDropBoxId, gold.IndividualDropBoxId, gold.Probability), Is.EqualTo((ExpType.rareChest, 0, 382000001, 0.002f)));
        Assert.That((wood.ExpType, wood.IndividualDropBoxId), Is.EqualTo((ExpType.normalChest, 382000002)));
    }

    [Test]
    public void ValorItemBoxNpcRankEnchant() {
        Assert.That(Row("11", "0.1", "<v improveRewardRate=\"1\" />").RewardRate, Is.EqualTo(1f));
        Assert.That(Row("8", "0.1", "<v itemIDs=\"30001133\"/>").ItemIds, Is.EqualTo(new[] { 30001133 }));
        SpecialEffectMetadata rank = Row("2", "", "<v npcRank=\"3,7\" reduceRate=\"0.06\" />");
        Assert.That((rank.Probability, rank.ReduceRate), Is.EqualTo((1f, 0.06f)));
        Assert.That(rank.NpcRanks, Is.EqualTo(new[] { 3, 7 }));
        SpecialEffectMetadata enchant = Row("5", "0.1", "<v enchantLevel=\"10\" improveRate=\"1\" />");
        Assert.That((enchant.EnchantLevel, enchant.ImproveRate), Is.EqualTo((10, 1f)));
    }

    [Test]
    public void TypesWithoutValues_AreKept() {
        Assert.That(Row("3", "", "").Type, Is.EqualTo(3));
        Assert.That(Row("3", "0.05", "", "s_additionalEffect_Special_110001014").Probability, Is.EqualTo(0.05f));
        Assert.That(Row("6", "0.5", "").Type, Is.EqualTo(6));
        Assert.That(Row("12", "", "").Type, Is.EqualTo(12));
    }

    [Test]
    public void CooldownGroupsAndNoType_AreNotRead() {
        Assert.That(SpecialEffects.Read("4", "", "<v groupID=\"90000001\" reduceRate=\"0.20\" />", ""), Is.Null);
        Assert.That(SpecialEffects.Read("0", "", "", ""), Is.Null);
        Assert.That(SpecialEffects.Read("", "", "", ""), Is.Null);
    }

    [Test]
    public void ClientExpTypes() {
        Assert.That(SpecialEffects.ClientExpType(4), Is.EqualTo(ExpType.rareChest));
        Assert.That(SpecialEffects.ClientExpType(5), Is.EqualTo(ExpType.rareChestFirst));
        Assert.That(SpecialEffects.ClientExpType(6), Is.EqualTo(ExpType.normalChest));
        Assert.That(SpecialEffects.ClientExpType(8), Is.EqualTo(ExpType.none));
    }

    // 110001016 / 110001017 have no probability, which is 1: the old read skipped them.
    [Test]
    public void CooldownGroupRate_AbsentProbabilityIsOne() {
        List<CooldownGroupRate> found = CooldownGroupRates.Read([("4", "", "<v groupID=\"90000001\" reduceRate=\"0.20\" />")]);
        Assert.That(found, Is.EqualTo(new[] { new CooldownGroupRate(90000001, 0.2f) }));
    }
}
