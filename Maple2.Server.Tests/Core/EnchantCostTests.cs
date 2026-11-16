using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Formulas;
using Maple2.Server.Tests.Game;

namespace Maple2.Server.Tests.Core;

// The onyx, chaos onyx and crystal fragment cost of an enchant above level 50, by rarity and level, then by the
// item's type: weapons and overalls double, gloves and shoes a fraction, hats two thirds.
public class EnchantCostTests {
    private static Item Gear(int id, int level, int rarity) {
        ItemMetadata metadata = TestGameSession.Metadata(id, EquipSlot.CL);
        metadata = metadata with { Limit = metadata.Limit with { Level = level } };
        return new Item(metadata, rarity, initialize: false);
    }

    private static (int, int, int) Cost(int id, int level, int rarity) {
        var costs = Enchant.GetEnchantCost(Gear(id, level, rarity)).ToDictionary(cost => cost.Tag, cost => cost.Amount);
        return (costs.GetValueOrDefault(ItemTag.Onix), costs.GetValueOrDefault(ItemTag.ChaosOnix), costs.GetValueOrDefault(ItemTag.CrystalPiece));
    }

    private const int Clothes = 11400001;
    private const int Hat = 11300001;
    private const int Gloves = 11600001;
    private const int Overall = 12200001;
    private const int Dagger = 13100001;
    private const int Bludgeon = 13000001;
    private const int Earring = 11200001;

    [TestCase(Clothes, 60, 3, 0, 0, 0)]
    [TestCase(Clothes, 60, 4, 636, 2, 16)]
    [TestCase(Clothes, 60, 5, 910, 3, 19)]
    [TestCase(Clothes, 70, 5, 1272, 8, 96)]
    [TestCase(Clothes, 60, 6, 1908, 12, 24)]
    [TestCase(Clothes, 70, 6, 1908, 12, 144)]
    [TestCase(Dagger, 70, 6, 1908, 12, 144)]
    [TestCase(Earring, 70, 6, 1908, 12, 144)]
    [TestCase(Overall, 70, 6, 3816, 24, 288)]
    [TestCase(Bludgeon, 70, 6, 3816, 24, 288)]
    [TestCase(Hat, 70, 6, 1259, 7, 95)]
    [TestCase(Gloves, 70, 6, 342, 2, 25)]
    public void HighLevel(int id, int level, int rarity, int onyx, int chaos, int crystal) {
        Assert.That(Cost(id, level, rarity), Is.EqualTo((onyx, chaos, crystal)));
    }

    // Levels 20-50: a curve by level times the rarity's share; onyx is never under 1, and a better rarity never costs less.
    [TestCase(20)]
    [TestCase(30)]
    [TestCase(50)]
    public void LowLevel_EveryRarityCosts_NeverFallsWithRarity(int level) {
        var onyx = new int[7];
        for (int rarity = 1; rarity <= 6; rarity++) {
            onyx[rarity] = Cost(Clothes, level, rarity).Item1;
            Assert.That(onyx[rarity], Is.GreaterThanOrEqualTo(1));
            Assert.That(onyx[rarity], Is.GreaterThanOrEqualTo(onyx[rarity - 1]));
        }
    }

    // Each enchant level scales the base cost by its own share of the level-70 tables.
    [TestCase(0, 1908, 12, 144)]
    [TestCase(5, 3324, 27, 252)]
    [TestCase(9, 10890, 96, 1080)]
    [TestCase(13, 21780, 192, 2160)]
    public void EnchantLevel_Scales(int enchants, int onyx, int chaos, int crystal) {
        Item item = Gear(Clothes, 70, 6);
        item.Enchant = new ItemEnchant(enchants);
        var costs = Enchant.GetEnchantCost(item).ToDictionary(cost => cost.Tag, cost => cost.Amount);
        Assert.That((costs[ItemTag.Onix], costs[ItemTag.ChaosOnix], costs[ItemTag.CrystalPiece]), Is.EqualTo((onyx, chaos, crystal)));
    }

    [Test]
    public void LowLevelEnchanted_UsesTheLevel50Tables() {
        Item plain = Gear(Clothes, 40, 4);
        Item enchanted = Gear(Clothes, 40, 4);
        enchanted.Enchant = new ItemEnchant(10);
        int Onyx(Item item) => Enchant.GetEnchantCost(item).Single(cost => cost.Tag == ItemTag.Onix).Amount;
        Assert.That(Onyx(enchanted), Is.EqualTo((int) (Onyx(plain) * 3.718f)).Within(1));
    }
}
