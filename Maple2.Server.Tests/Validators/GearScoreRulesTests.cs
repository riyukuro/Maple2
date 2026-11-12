using Maple2.Model.Validators;

namespace Maple2.Server.Tests.Validators;

// The gear score of the client's own computation; the totals are the changes KMS2 sends for the same items.
public class GearScoreRulesTests {
    private static int Level(int gearScore, int dataRank, int rarity, int subType, int enchants = 0, int limitBreak = 0) {
        return GearScoreRules.ItemLevel(gearScore, dataRank, rarity, subType, enchants, limitBreak).Level;
    }

    // Gear score changes KMS2 sends when one item is put on or taken off.
    [TestCase(22, 3, 32, 230)] // longsword 13200235
    [TestCase(16, 3, 32, 170)] // longsword 13200232
    [TestCase(24, 3, 32, 250)] // longsword 13200236
    [TestCase(10, 3, 32, 110)] // longsword 13200229
    [TestCase(14, 3, 13, 31)] // cap 11300962
    [TestCase(30, 3, 15, 93)] // pants 11500752
    [TestCase(10, 3, 16, 6)] // gloves 11600953
    [TestCase(70, 5, 21, 1779)] // 12100110
    public void ItemLevel_IsTheChangeKms2Sends(int gearScore, int rank, int subType, int expected) {
        Assert.That(Level(gearScore, rank, rank, subType), Is.EqualTo(expected));
    }

    // The item data's rank goes before the item's own rarity: a bow of gear score 80 and data rank 6 at rarity 4
    // is 42147 in the client's window.
    [Test]
    public void ItemLevel_UsesTheDataRank() {
        Assert.That(Level(80, 6, 4, 51), Is.EqualTo(42147));
        Assert.That(Level(80, 0, 4, 51), Is.EqualTo(12378));
    }

    // The subtype is the client's item data subtype, which is the id's two digits after the first; the server
    // data has 16 for the bow 15100304, the gloves' factor (2528 instead of the client window's 42147).
    [TestCase(15100304, 51)]
    [TestCase(11200001, 12)]
    [TestCase(13400010, 34)]
    [TestCase(10200001, 2)]
    public void SubType_IsTheIdsDigits(int itemId, int expected) {
        Assert.That(GearScoreRules.SubType(itemId), Is.EqualTo(expected));
    }

    // The bow 15100304 (gear score 80, data rank 6, rarity 4) as the client's window shows it: 42147 plain,
    // 127285 with limit break 1.
    [Test]
    public void ItemLevel_OfTheBowByItsId() {
        int subType = GearScoreRules.SubType(15100304);
        Assert.Multiple(() => {
            Assert.That(GearScoreRules.ItemLevel(80, 6, 4, subType, 0, 0).Level, Is.EqualTo(42147));
            Assert.That(GearScoreRules.ItemLevel(80, 6, 5, subType, 0, 1).Level, Is.EqualTo(127285));
            Assert.That(GearScoreRules.ItemLevel(80, 6, 4, 16, 0, 0).Level, Is.EqualTo(2528));
        });
    }

    [Test]
    public void ItemLevel_AddsTheEnchantAndTheLimitBreak() {
        // From gear score 70: +15 doubles the base.
        Assert.That(GearScoreRules.ItemLevel(80, 6, 6, 51, 15, 0), Is.EqualTo((42147 + 84294, 84294)));
        Assert.That(GearScoreRules.ItemLevel(80, 4, 4, 51, 10, 0), Is.EqualTo((12378 + 6189, 6189)));
        // Below 70 only data rank 4 or more has an enchant part.
        Assert.That(GearScoreRules.ItemLevel(50, 4, 4, 32, 10, 0), Is.EqualTo((1030 + 360, 360)));
        Assert.That(GearScoreRules.ItemLevel(50, 3, 3, 32, 10, 0), Is.EqualTo((510, 0)));
        Assert.That(GearScoreRules.ItemLevel(60, 5, 5, 14, 0, 0), Is.EqualTo((1113, 0)));
        Assert.That(GearScoreRules.ItemLevel(80, 6, 6, 51, 0, 1), Is.EqualTo((42147 + 85138, 85138)));
        Assert.That(GearScoreRules.ItemLevel(0, 6, 6, 51, 15, 0), Is.EqualTo((0, 0)));
    }

    // The client script's values.
    [TestCase(1, 85138)]
    [TestCase(19, 100312)]
    [TestCase(20, 101576)]
    [TestCase(40, 127286)]
    [TestCase(50, 144567)]
    [TestCase(60, 166062)]
    [TestCase(70, 191772)]
    [TestCase(80, 221698)]
    [TestCase(90, 256259)]
    [TestCase(99, 294192)]
    [TestCase(100, 0)]
    [TestCase(0, 0)]
    public void LimitBreakLevel_IsTheScripts(int level, int expected) {
        Assert.That(GearScoreRules.LimitBreakLevel(level), Is.EqualTo(expected));
    }

    // The client script's values for two paired weapons.
    [TestCase(1000, 1000, 1000)]
    [TestCase(1000, 1400, 1200)]
    [TestCase(1000, 1428, 1214)]
    [TestCase(1000, 1429, 1150)]
    [TestCase(700, 1000, 850)]
    [TestCase(69, 100, 79)]
    [TestCase(5000, 1000, 1400)]
    [TestCase(0, 5000, 2500)]
    [TestCase(110, 115, 112)]
    public void PairLevel_IsTheScripts(int first, int second, int expected) {
        Assert.That(GearScoreRules.PairLevel(first, second), Is.EqualTo(expected));
    }

    // KMS2: one dagger of level 70 with nothing else worn is 17, a second one of the same level makes 70;
    // throwing stars of 110 and 115 are 112, two of 115 are 115.
    [Test]
    public void Total_PairedWeapons() {
        Assert.That(GearScoreRules.Total([new GearScoreRules.Worn(1, true, 70, 0)], 4f), Is.EqualTo(17));
        Assert.That(GearScoreRules.Total([new GearScoreRules.Worn(1, true, 70, 0), new GearScoreRules.Worn(2, true, 70, 0)], 4f), Is.EqualTo(70));
        Assert.That(GearScoreRules.Total([new GearScoreRules.Worn(2, true, 110, 0), new GearScoreRules.Worn(1, true, 115, 0)], 4f), Is.EqualTo(112));
        Assert.That(GearScoreRules.Total([new GearScoreRules.Worn(2, true, 115, 0), new GearScoreRules.Worn(1, true, 115, 0)], 4f), Is.EqualTo(115));
    }

    // A shield or spellbook in the left hand adds nothing; the other slots add up.
    [Test]
    public void Total_OtherSlots() {
        Assert.That(GearScoreRules.Total([
            new GearScoreRules.Worn(1, false, 230, 0), new GearScoreRules.Worn(2, false, 500, 0),
            new GearScoreRules.Worn(0, false, 31, 0), new GearScoreRules.Worn(0, false, 93, 0),
        ], 4f), Is.EqualTo(354));
        Assert.That(GearScoreRules.Total([], 4f), Is.EqualTo(0));
    }
}
