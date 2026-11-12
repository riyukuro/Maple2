using System.Collections.Generic;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Formulas;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Core;

// Client: CItemData::GetSellPrice (commonfiles/itemdata.cpp:1508) and the multicontrol table.
public class SellPriceTests {
    // The NA table's equipmentSellPrice rows and the KR table, which has no such row.
    private static readonly (string, string, string)[] NaRows = [
        ("enchantScrollReceipe", "1", "0.5"),
        ("equipmentSellPrice", "1", "0.333"), ("equipmentSellPrice", "2", "0.333"), ("equipmentSellPrice", "3", "0.333"),
        ("equipmentSellPrice", "4", "1"), ("equipmentSellPrice", "5", "1"),
    ];
    private static readonly (string, string, string)[] KrRows = [
        ("enchantScrollReceipe", "0", "1"), ("questRewardMeso", "4", "0"),
    ];

    private static ConstantsTable Constants((string, string, string)[] rows, bool kr) {
        return new ConstantsTable(
            SellPriceNormalMax: kr ? 5693 : 4628, SellPriceRareMax: kr ? 7116 : 5785, SellPriceEliteMax: kr ? 9109 : 7405,
            SellPriceExcellentMax: kr ? 11386 : 9256, SellPriceLegendaryMax: kr ? 13948 : 11339, SellPriceArtifactMax: kr ? 16794 : 13653) {
            EquipmentSellPriceFactors = ConstantsTable.ReadMultiControl(rows, "equipmentSellPrice"),
        };
    }

    private static ItemMetadata Item(int type, int level, long[] prices, long[] custom, int skinType = 0) {
        var property = new ItemMetadataProperty(skinType != 0, skinType, 1, type, 0, string.Empty, string.Empty, ItemTag.None, 0, 0, 0, 0, 0, 0, 0, 0, 0, [], false, 0, false, [], prices, custom, 0, 0);
        var limit = new ItemMetadataLimit(Gender.All, level, 0, 4, true, true, true, true, true, false, false, 0, [], []);
        return new ItemMetadata(1, null, [], "", [], new ItemMetadataLife(0, 0), property, new ItemMetadataCustomize(0, 0), limit, null, null, [],
            null, null, null, null);
    }

    private static readonly long[] GearPrices = [961, 1201, 1538, 1922, 2354, 2835];
    private static readonly long[] None = [0, 0, 0, 0, 0, 0];

    [Test]
    public void MultiControl_FirstRowOfAKeyStays_MissingValueIsOne() {
        Dictionary<int, float> factors = ConstantsTable.ReadMultiControl([
            ("equipmentSellPrice", "1", "0.333"), ("equipmentSellPrice", "1", "0.5"), ("equipmentSellPrice", "2", ""),
            ("other", "3", "0.1"), ("equipmentSellPrice", "x", "0.1"),
        ], "equipmentSellPrice");

        Assert.That(factors, Has.Count.EqualTo(2));
        Assert.That(factors[1], Is.EqualTo(0.333f));
        Assert.That(factors[2], Is.EqualTo(1f));
    }

    [TestCase(1, 961)]
    [TestCase(2, 1201)]
    [TestCase(3, 1538)]
    [TestCase(4, 1922)]
    [TestCase(6, 2835)]
    public void Kr_EquipmentSellsAtTheListPrice(int rarity, long expected) {
        Assert.That(Shop.SellPrice(Item(1, 50, GearPrices, None), rarity, Constants(KrRows, kr: true)), Is.EqualTo(expected));
    }

    [TestCase(1, 320)] // 961 x 0.333 = 320.01
    [TestCase(2, 399)] // 1201 x 0.333 = 399.93
    [TestCase(3, 512)] // 1538 x 0.333 = 512.15
    [TestCase(4, 1922)]
    [TestCase(6, 2835)] // no row for rarity 6
    public void Na_EquipmentOfRarityOneToThreeSellsAtAThird(int rarity, long expected) {
        Assert.That(Shop.SellPrice(Item(1, 50, GearPrices, None), rarity, Constants(NaRows, kr: false)), Is.EqualTo(expected));
    }

    [Test]
    public void OtherItemTypes_AreNotMultiplied_AndTakeTheCustomPrice() {
        ItemMetadata potion = Item(2, 0, [664, 664, 664, 664, 664, 664], [1, 1, 1, 1, 1, 1]);
        Assert.That(Shop.SellPrice(potion, 1, Constants(NaRows, kr: false)), Is.EqualTo(1));
        Assert.That(Shop.SellPrice(potion, 1, Constants(KrRows, kr: true)), Is.EqualTo(1));

        ItemMetadata material = Item(2, 60, GearPrices, None);
        Assert.That(Shop.SellPrice(material, 2, Constants(NaRows, kr: false)), Is.EqualTo(1201));
    }

    [Test]
    public void CustomPrice_ReplacesTheListPriceOnlyWhereItIsSet() {
        ItemMetadata gear = Item(1, 50, GearPrices, [700, 0, 0, 0, 0, 0]);
        Assert.That(Shop.SellPrice(gear, 1, Constants(KrRows, kr: true)), Is.EqualTo(700));
        Assert.That(Shop.SellPrice(gear, 2, Constants(KrRows, kr: true)), Is.EqualTo(1201));
    }

    [Test]
    public void LevelLimit57_OnlyAPriceOfZeroTakesTheConstant() {
        // With a price the item's own price stands, also at level 57 and up.
        Assert.That(Shop.SellPrice(Item(1, 60, GearPrices, None), 4, Constants(NaRows, kr: false)), Is.EqualTo(1922));
        Assert.That(Shop.SellPrice(Item(1, 60, GearPrices, None), 1, Constants(KrRows, kr: true)), Is.EqualTo(961));

        ItemMetadata free = Item(1, 57, None, None);
        Assert.That(Shop.SellPrice(free, 1, Constants(NaRows, kr: false)), Is.EqualTo(1541)); // 4628 x 0.333
        Assert.That(Shop.SellPrice(free, 3, Constants(NaRows, kr: false)), Is.EqualTo(2465)); // 7405 x 0.333
        Assert.That(Shop.SellPrice(free, 4, Constants(NaRows, kr: false)), Is.EqualTo(9256));
        Assert.That(Shop.SellPrice(free, 1, Constants(KrRows, kr: true)), Is.EqualTo(5693));
        Assert.That(Shop.SellPrice(free, 6, Constants(KrRows, kr: true)), Is.EqualTo(16794));

        // Not below 57, not for a skin type, not for another item type.
        Assert.That(Shop.SellPrice(Item(1, 56, None, None), 1, Constants(KrRows, kr: true)), Is.EqualTo(0));
        Assert.That(Shop.SellPrice(Item(1, 60, None, None, skinType: 1), 1, Constants(KrRows, kr: true)), Is.EqualTo(0));
        Assert.That(Shop.SellPrice(Item(2, 60, None, None), 1, Constants(KrRows, kr: true)), Is.EqualTo(0));
    }

    [Test]
    public void TableWithoutTheFactors_AndRarityOutsideTheList() {
        var old = new ConstantsTable();
        Assert.That(Shop.SellPrice(Item(1, 50, GearPrices, None), 1, old), Is.EqualTo(961));
        Assert.That(Shop.SellPrice(Item(1, 50, GearPrices, None), 0, old), Is.EqualTo(0));
        Assert.That(Shop.SellPrice(Item(1, 50, GearPrices, None), 7, old), Is.EqualTo(0));
        Assert.That(Shop.SellPrice(Item(1, 50, [], []), 1, old), Is.EqualTo(0));
    }

    // The table is stored as JSON: the factors come back, and a table stored before the field has none.
    [Test]
    public void Factors_SurviveTheJsonStore() {
        string json = System.Text.Json.JsonSerializer.Serialize(Constants(NaRows, kr: false));
        ConstantsTable? back = System.Text.Json.JsonSerializer.Deserialize<ConstantsTable>(json);
        Assert.That(back!.EquipmentSellPriceFactor(1), Is.EqualTo(0.333f));
        Assert.That(back.EquipmentSellPriceFactor(4), Is.EqualTo(1f));

        ConstantsTable? old = System.Text.Json.JsonSerializer.Deserialize<ConstantsTable>("{\"SellPriceNormalMax\":4628}");
        Assert.That(old!.EquipmentSellPriceFactors, Is.Null);
        Assert.That(old.EquipmentSellPriceFactor(1), Is.EqualTo(1f));
    }

    // Client: UIShopDialog::CanSellItem (client/ms2/uishopdialog.cpp:2076).
    [TestCase(true, false, 0, false, true)]
    [TestCase(false, false, 0, false, false)] // the item data forbids the shop sale
    [TestCase(true, true, 900, false, false)] // locked
    [TestCase(true, false, 1001, false, false)] // unlocking, the time not reached
    [TestCase(true, false, 1000, false, true)] // unlocked
    [TestCase(true, false, 0, true, false)] // a gemstone in a socket
    public void CanSell(bool shopSell, bool locked, long unlockTime, bool hasGem, bool expected) {
        Assert.That(ShopRules.CanSell(shopSell, locked, unlockTime, 1000, hasGem), Is.EqualTo(expected));
    }

    [TestCase(0, 999, true)] // on a schedule, the time passed
    [TestCase(0, 1000, false)]
    [TestCase(0, long.MaxValue, false)]
    [TestCase(500, 500, false)] // a fixed time in the data
    public void RestockDue(long fixedTime, long restockTime, bool expected) {
        Assert.That(ShopRules.RestockDue(fixedTime, restockTime, 1000), Is.EqualTo(expected));
    }
}
