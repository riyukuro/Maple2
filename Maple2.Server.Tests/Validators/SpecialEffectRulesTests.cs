using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;

namespace Maple2.Server.Tests.Validators;

// The KR rows' special effects (SpecialEffectsTests reads them).
public class SpecialEffectRulesTests {
    internal static readonly SpecialEffectMetadata Fishing4 = new(9, 1, "", ExpRate: 0.04f); // 110000101
    internal static readonly SpecialEffectMetadata FishingBig = new(9, 1, "s_additionalEffect_Special_110001027", ExpRate: 1, MasteryRate: 1, OnlyBigSize: true);
    internal static readonly SpecialEffectMetadata Music4 = new(10, 1, "", ExpRate: 0.04f); // 110000105
    internal static readonly SpecialEffectMetadata MusicChance = new(10, 0.05f, "s_additionalEffect_Special_110001025", ExpRate: 2);
    internal static readonly SpecialEffectMetadata QuestExp = new(7, 0.05f, "s_additionalEffect_Special_110001021", RepeatType: 2, ExpRate: 4);
    internal static readonly SpecialEffectMetadata QuestMeso = new(7, 0.05f, "s_additionalEffect_Special_110001022", RepeatType: 2, MesoRate: 4);
    internal static readonly SpecialEffectMetadata QuestRue = new(7, 0.05f, "s_additionalEffect_Special_110001023", RepeatType: 2, RueRate: 4);
    internal static readonly SpecialEffectMetadata Valor = new(11, 0.1f, "s_additionalEffect_Special_110001015", RewardRate: 1);
    internal static readonly SpecialEffectMetadata GoldChest = new(13, 0.002f, "s_additionalEffect_Special_110001019", ExpType: ExpType.rareChest, IndividualDropBoxId: 382000001);
    internal static readonly SpecialEffectMetadata WoodChest = new(13, 0.006f, "s_additionalEffect_Special_110001020", ExpType: ExpType.normalChest, IndividualDropBoxId: 382000002);
    internal static readonly SpecialEffectMetadata PrestigeBox = new(8, 0.1f, "s_additionalEffect_Special_110001024", ItemIds: [30001133]);
    internal static readonly SpecialEffectMetadata EliteRank = new(2, 1, "", NpcRanks: [3, 7], ReduceRate: 0.06f); // 110001013
    internal static readonly SpecialEffectMetadata WorldBossDouble = new(3, 1, ""); // 110000007
    internal static readonly SpecialEffectMetadata WorldBossChance = new(3, 0.05f, "s_additionalEffect_Special_110001014");
    internal static readonly SpecialEffectMetadata CityRest = new(12, 1, ""); // 110001026

    // Its NextDouble is the value given.
    internal sealed class FixedRandom(double value) : Random {
        public override double NextDouble() => value;
    }

    private static readonly Random Wins = new FixedRandom(0);
    private static readonly Random Loses = new FixedRandom(0.999);

    [Test]
    public void Rolls_ByProbability() {
        Assert.That(SpecialEffectRules.Rolls(QuestExp, new FixedRandom(0.049)), Is.True);
        Assert.That(SpecialEffectRules.Rolls(QuestExp, new FixedRandom(0.051)), Is.False);
        Assert.That(SpecialEffectRules.Rolls(Fishing4, Loses), Is.True, "probability 1");
    }

    [Test]
    public void Taken_OfTheTypeThatApplyAndRoll() {
        SpecialEffectMetadata[] held = [Fishing4, FishingBig, Music4, QuestExp];
        Assert.That(SpecialEffectRules.Taken(held, SpecialEffectRules.Fishing, Loses), Is.EqualTo(new[] { Fishing4, FishingBig }));
        Assert.That(SpecialEffectRules.Taken(held, SpecialEffectRules.QuestReward, Loses), Is.Empty);
        Assert.That(SpecialEffectRules.Taken(held, SpecialEffectRules.QuestReward, Wins), Is.EqualTo(new[] { QuestExp }));
        Assert.That(SpecialEffectRules.Taken(held, SpecialEffectRules.Fishing, Wins, effect => !effect.OnlyBigSize), Is.EqualTo(new[] { Fishing4 }));
    }

    [Test]
    public void Messages_EachOnceAndNotEmpty() {
        Assert.That(SpecialEffectRules.Messages([Fishing4, FishingBig, FishingBig]), Is.EqualTo(new[] { "s_additionalEffect_Special_110001027" }));
    }

    [Test]
    public void Fishing_BigSizeOnPrizeFish() {
        Assert.That(SpecialEffectRules.FishingApplies(FishingBig, prizeFish: false), Is.False);
        Assert.That(SpecialEffectRules.FishingApplies(FishingBig, prizeFish: true), Is.True);
        Assert.That(SpecialEffectRules.FishingApplies(Fishing4, prizeFish: false), Is.True);
        Assert.That(SpecialEffectRules.FishingFactors([Fishing4, FishingBig]), Is.EqualTo((2.04f, 2f)));
        Assert.That(SpecialEffectRules.FishingFactors([]), Is.EqualTo((1f, 1f)));
    }

    [Test]
    public void Music_ChanceNeedsTheWholeScore() {
        Assert.That(SpecialEffectRules.MusicApplies(MusicChance, wholeScore: false), Is.False);
        Assert.That(SpecialEffectRules.MusicApplies(MusicChance, wholeScore: true), Is.True);
        Assert.That(SpecialEffectRules.MusicApplies(Music4, wholeScore: false), Is.True);
        Assert.That(SpecialEffectRules.ExpFactor([Music4, MusicChance]), Is.EqualTo(3.04f));
    }

    [Test]
    public void Quest_DailyRewardsTimesFiveAtRate4() {
        Assert.That(SpecialEffectRules.QuestApplies(QuestMeso, questRepeat: 2, exp: 100, meso: 1000, rue: 0), Is.True);
        Assert.That(SpecialEffectRules.QuestApplies(QuestMeso, questRepeat: 1, exp: 100, meso: 1000, rue: 0), Is.False, "not daily");
        Assert.That(SpecialEffectRules.QuestApplies(QuestRue, questRepeat: 2, exp: 100, meso: 1000, rue: 0), Is.False, "no rue");
        Assert.That(SpecialEffectRules.QuestExtra([QuestExp, QuestMeso], exp: 100, meso: 1000, rue: 7), Is.EqualTo((400L, 4000L, 0L)));
        Assert.That(SpecialEffectRules.QuestExtra([QuestRue], exp: 100, meso: 1000, rue: 7), Is.EqualTo((0L, 0L, 28L)));
    }

    [Test]
    public void Valor_RewardDoubled() {
        Assert.That(SpecialEffectRules.ValorExtra([Valor], 30), Is.EqualTo(30));
        Assert.That(SpecialEffectRules.ValorExtra([], 30), Is.EqualTo(0));
        Assert.That(SpecialEffectRules.ValorExtra([Valor], -5), Is.EqualTo(0), "a loss");
    }

    [Test]
    public void Chest_ExactExpType() {
        Assert.That(SpecialEffectRules.ChestApplies(GoldChest, ExpType.rareChest), Is.True);
        Assert.That(SpecialEffectRules.ChestApplies(GoldChest, ExpType.rareChestFirst), Is.False);
        Assert.That(SpecialEffectRules.ChestApplies(WoodChest, ExpType.normalChest), Is.True);
        Assert.That(SpecialEffectRules.ChestApplies(WoodChest, ExpType.rareChest), Is.False);
    }

    [Test]
    public void ItemBox_OfItsIds() {
        Assert.That(SpecialEffectRules.ItemBoxApplies(PrestigeBox, 30001133), Is.True);
        Assert.That(SpecialEffectRules.ItemBoxApplies(PrestigeBox, 30001134), Is.False);
    }

    [Test]
    public void NpcRank_EliteDamageLess6Percent() {
        Assert.That(SpecialEffectRules.NpcRankDamageFactor([EliteRank], 3, Loses), Is.EqualTo(0.94f));
        Assert.That(SpecialEffectRules.NpcRankDamageFactor([EliteRank], 7, Loses), Is.EqualTo(0.94f));
        Assert.That(SpecialEffectRules.NpcRankDamageFactor([EliteRank], 1, Loses), Is.EqualTo(1f));
        Assert.That(SpecialEffectRules.NpcRankDamageFactor([], 3, Loses), Is.EqualTo(1f));
    }

    // 110000007 always one more of a world boss's drops, 110001014 one more at 5 %; both held and won, two more.
    [Test]
    public void WorldBoss_OneMorePerEffectTaken() {
        Assert.That(SpecialEffectRules.WorldBossExtraDrops(SpecialEffectRules.Taken([WorldBossDouble], 3, Loses)), Is.EqualTo(1));
        Assert.That(SpecialEffectRules.WorldBossExtraDrops(SpecialEffectRules.Taken([WorldBossChance], 3, Loses)), Is.EqualTo(0));
        Assert.That(SpecialEffectRules.WorldBossExtraDrops(SpecialEffectRules.Taken([WorldBossChance], 3, new FixedRandom(0.049))), Is.EqualTo(1));
        Assert.That(SpecialEffectRules.WorldBossExtraDrops(SpecialEffectRules.Taken([WorldBossDouble, WorldBossChance, Valor], 3, Wins)), Is.EqualTo(2));
    }

    // 110001026's nine KR cities (Tria 02000001 ... Ludari 02010002, Babeni 02020041); not their quest copies or another field.
    [TestCase(2000001, true)]
    [TestCase(2000023, true)]
    [TestCase(2000051, true)]
    [TestCase(2000062, true)]
    [TestCase(2000076, true)]
    [TestCase(2000100, true)]
    [TestCase(2000270, true)]
    [TestCase(2010002, true)]
    [TestCase(2020041, true)]
    [TestCase(52000001, false)]
    [TestCase(2000002, false)]
    [TestCase(62000000, false)]
    public void Rest_TheTooltipsCities(int mapId, bool city) {
        Assert.That(SpecialEffectRules.RestCity(mapId), Is.EqualTo(city));
    }

    [Test]
    public void TypesNotApplied_AreNotTaken() {
        SpecialEffectMetadata[] held = [new(6, 0.5f, ""), new(5, 0.1f, "", EnchantLevel: 10, ImproveRate: 1)];
        int[] used = [SpecialEffectRules.NpcRankDamage, SpecialEffectRules.WorldBossReward, SpecialEffectRules.QuestReward,
            SpecialEffectRules.ItemBoxAgain, SpecialEffectRules.Fishing, SpecialEffectRules.Music, SpecialEffectRules.ValorToken,
            SpecialEffectRules.Rest, SpecialEffectRules.ChestDropBox];
        Assert.That(used.SelectMany(type => SpecialEffectRules.Taken(held, type, Wins)), Is.Empty);
    }
}
