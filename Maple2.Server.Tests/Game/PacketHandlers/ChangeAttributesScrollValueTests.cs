using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.PacketHandlers;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The remake scroll's row (ChangeAttributesScrollHandler.ScrollFits): the client refuses a row without an option
// value (addOpValue 0) as invalid scroll data (error 11) once the item fits its types, rarities and levels.
public class ChangeAttributesScrollValueTests {
    private static Item Accessory(int rarity = 4) => new(TestGameSession.Metadata(11200001), rarity: rarity) { Stats = new ItemStats() };

    private static ItemRemakeScrollMetadata Row(Item item, RollValueType value) {
        short level = (short) item.Metadata.Limit.Level;
        return new ItemRemakeScrollMetadata(MinLevel: level, MaxLevel: level, ItemTypes: [item.Type.Type], Rarities: [4],
            RollAttribute: false, RollValueType: value, OnlyPet: false);
    }

    // Before, a row without a value was used.
    [Test]
    public void NoValue_InvalidScrollData() {
        Item item = Accessory();
        Assert.That(ChangeAttributesScrollHandler.ScrollFits(item, Row(item, RollValueType.None)),
            Is.EqualTo(ChangeAttributesScrollError.s_itemremake_scroll_error_invalid_scroll_data));
    }

    [TestCase(RollValueType.Normal)]
    [TestCase(RollValueType.HigherEnd)]
    public void Value_Fits(RollValueType value) {
        Item item = Accessory();
        Assert.That(ChangeAttributesScrollHandler.ScrollFits(item, Row(item, value)), Is.EqualTo(ChangeAttributesScrollError.none));
    }

    // A scroll whose remake id has no row is refused with 10 (FUN_140616170, itemremakescrolldata.cpp:287), as is
    // one without a numeric id; a row is found by the scroll's parameter. Before, both were 11.
    [Test]
    public void MissingRow_InvalidScroll() {
        Item Scroll(string parameter) => new(TestGameSession.Metadata(20300001,
            function: new ItemMetadataFunction(ItemFunction.ItemRemakeScroll, "ItemRemakeScroll", parameter, false)));
        Item item = Accessory();
        var table = new ItemRemakeScrollTable(new Dictionary<int, ItemRemakeScrollMetadata> { [5] = Row(item, RollValueType.Normal) });
        Assert.Multiple(() => {
            Assert.That(ChangeAttributesScrollHandler.ScrollRow(table, Scroll("6"), out _),
                Is.EqualTo(ChangeAttributesScrollError.s_itemremake_scroll_error_invalid_scroll));
            Assert.That(ChangeAttributesScrollHandler.ScrollRow(table, Scroll("x"), out _),
                Is.EqualTo(ChangeAttributesScrollError.s_itemremake_scroll_error_invalid_scroll));
            Assert.That(ChangeAttributesScrollHandler.ScrollRow(table, Scroll("5"), out ItemRemakeScrollMetadata? row),
                Is.EqualTo(ChangeAttributesScrollError.none));
            Assert.That(row, Is.Not.Null);
        });
    }

    // The item checks come first, as in the client, each with its own code (FUN_140616170: rank 5).
    [Test]
    public void NoValue_WrongRarity_ImpossibleRank() {
        Item item = Accessory(rarity: 3);
        Assert.That(ChangeAttributesScrollHandler.ScrollFits(item, Row(item, RollValueType.None) with { Rarities = [4] }),
            Is.EqualTo(ChangeAttributesScrollError.s_itemremake_scroll_error_impossible_rank));
    }

    // Each refusal has the client's code (itemremakescrolldata.cpp:283-355): the slot list (4), the rarities (5), the
    // levels (6), a pet row on an item that is no pet (1), an item without options (3). Before, all were 8.
    [Test]
    public void Refusals_HaveTheClientsCodes() {
        Item item = Accessory();
        ItemRemakeScrollMetadata row = Row(item, RollValueType.Normal);
        Item bare = new(TestGameSession.Metadata(11200001), rarity: 4);
        Assert.Multiple(() => {
            Assert.That(ChangeAttributesScrollHandler.ScrollFits(item, row with { ItemTypes = [13] }),
                Is.EqualTo(ChangeAttributesScrollError.s_itemremake_scroll_error_impossible_slot));
            Assert.That(ChangeAttributesScrollHandler.ScrollFits(item, row with { Rarities = [5] }),
                Is.EqualTo(ChangeAttributesScrollError.s_itemremake_scroll_error_impossible_rank));
            Assert.That(ChangeAttributesScrollHandler.ScrollFits(item, row with { MinLevel = (short) (row.MaxLevel + 1), MaxLevel = (short) (row.MaxLevel + 1) }),
                Is.EqualTo(ChangeAttributesScrollError.s_itemremake_scroll_error_impossible_level));
            Assert.That(ChangeAttributesScrollHandler.ScrollFits(item, row with { OnlyPet = true }),
                Is.EqualTo(ChangeAttributesScrollError.s_itemremake_scroll_error_invalid_target));
            Assert.That(ChangeAttributesScrollHandler.ScrollFits(bare, row),
                Is.EqualTo(ChangeAttributesScrollError.s_itemremake_scroll_error_invalid_target_stat));
        });
    }

    // The row alone decides: the rank 3 pet rows (10000040, 10000097) take a rank 3 pet of level 50. Before, the
    // attribute dialog's gate (rarity 4 to 6) refused it first, so those scrolls could never be used.
    [Test]
    public void RankThreePetRow_TakesARankThreePet() {
        Item pet = new(TestGameSession.Metadata(61000001), rarity: 3) { Stats = new ItemStats() };
        short level = (short) pet.Metadata.Limit.Level;
        var row = new ItemRemakeScrollMetadata(MinLevel: level, MaxLevel: level, ItemTypes: [], Rarities: [3],
            RollAttribute: false, RollValueType: RollValueType.Normal, OnlyPet: true);
        Assert.Multiple(() => {
            Assert.That(pet.Type.IsPet, Is.True);
            Assert.That(ChangeAttributesScrollHandler.ScrollFits(pet, row), Is.EqualTo(ChangeAttributesScrollError.none));
        });
    }

    // An item with remakeDisable is refused first (itemremakescrolldata.cpp:292, error 7), even with no options.
    // Before, the property was not ingested and such an item was remade.
    [Test]
    public void RemakeDisable_ImpossibleProperty() {
        Item item = Accessory();
        ItemRemakeScrollMetadata row = Row(item, RollValueType.Normal);
        ItemMetadata data = item.Metadata with { Property = item.Metadata.Property with { RemakeDisable = true } };
        Assert.Multiple(() => {
            Assert.That(ChangeAttributesScrollHandler.ScrollFits(new Item(data, rarity: 4) { Stats = new ItemStats() }, row),
                Is.EqualTo(ChangeAttributesScrollError.s_itemremake_scroll_error_impossible_property));
            Assert.That(ChangeAttributesScrollHandler.ScrollFits(new Item(data, rarity: 4), row),
                Is.EqualTo(ChangeAttributesScrollError.s_itemremake_scroll_error_impossible_property));
        });
    }

    // A row with an item list takes only those ids (error 8) and then checks neither the pet kind nor the slot list
    // (FUN_140616170, :297-313). Before, the list was not ingested and the slot list decided.
    [Test]
    public void ItemList_DecidesInsteadOfTheSlots() {
        Item item = Accessory();
        ItemRemakeScrollMetadata row = Row(item, RollValueType.Normal) with { ItemTypes = [13] };
        Assert.Multiple(() => {
            Assert.That(ChangeAttributesScrollHandler.ScrollFits(item, row with { ItemIds = [11200002] }),
                Is.EqualTo(ChangeAttributesScrollError.s_itemremake_scroll_error_impossible_item));
            Assert.That(ChangeAttributesScrollHandler.ScrollFits(item, row with { ItemIds = [11200001] }),
                Is.EqualTo(ChangeAttributesScrollError.none));
            Assert.That(ChangeAttributesScrollHandler.ScrollFits(item, row with { ItemIds = [11200001], OnlyPet = true }),
                Is.EqualTo(ChangeAttributesScrollError.none));
        });
    }

    // The attribute change dialog refuses an item with remakeDisable on both pages (item.cpp:1862 and :1903).
    [Test]
    public void AttributeDialog_RefusesRemakeDisable() {
        Item item = Accessory();
        ItemMetadata data = item.Metadata with {
            Property = item.Metadata.Property with { RemakeDisable = true },
            Limit = item.Metadata.Limit with { Level = 60 },
        };
        ItemMetadata open = data with { Property = data.Property with { RemakeDisable = false } };
        Assert.Multiple(() => {
            Assert.That(ChangeAttributesHandler.IsValidItem(new Item(open, rarity: 4)), Is.True);
            Assert.That(ChangeAttributesHandler.IsValidItem(new Item(data, rarity: 4)), Is.False);
        });
    }
    // The item page takes armor and accessories but no weapon (item.cpp:1850, result 8). Before, the server
    // changed a weapon's attributes.
    [TestCase(11200001, true)] // earring
    [TestCase(11300001, true)] // hat
    [TestCase(11400001, true)] // clothes
    [TestCase(12200001, true)] // overall
    [TestCase(13000001, false)] // one-handed weapon
    [TestCase(14000001, false)] // off-hand weapon
    [TestCase(15000001, false)] // two-handed weapon
    public void AttributeDialog_TakesNoWeapon(int id, bool valid) {
        ItemMetadata data = TestGameSession.Metadata(id);
        data = data with { Limit = data.Limit with { Level = 50 } };
        Assert.That(ChangeAttributesHandler.IsValidItem(new Item(data, rarity: 4)), Is.EqualTo(valid));
    }

    private static Item Rolled(int id, params BasicAttribute[] basics) {
        var random = new Dictionary<BasicAttribute, BasicOption>();
        foreach (BasicAttribute basic in basics) {
            random[basic] = new BasicOption(1);
        }
        var groups = new Dictionary<BasicAttribute, BasicOption>[5];
        groups[(int) ItemStats.Type.Random] = random;
        return new Item(TestGameSession.Metadata(id), rarity: 4) {
            Stats = new ItemStats(groups.Select(group => group ?? new Dictionary<BasicAttribute, BasicOption>()).ToArray(),
                Enumerable.Range(0, 5).Select(_ => new Dictionary<SpecialAttribute, SpecialOption>()).ToArray()),
        };
    }

    private static Item WithOption(int id, int rarity, int constantId) =>
        new(TestGameSession.Metadata(id) with {
            Option = new ItemMetadataOption(0, default, 61000019, default, constantId, default, 0, 0),
        }, rarity: rarity);

    // The table the option check reads is the client's (FUN_14053eff0, itemdata.cpp:1633): a pet's by its rarity, none
    // for rarity 1 or without a constant option, any other item's own RandomId. Before, a pet's RandomId (61000019) was
    // read at every rarity.
    [Test]
    public void RandomTable_PetsByRarity() {
        Assert.Multiple(() => {
            Assert.That(ChangeAttributesScrollHandler.RandomTableId(WithOption(61000001, 2, 1)), Is.EqualTo(61000001));
            Assert.That(ChangeAttributesScrollHandler.RandomTableId(WithOption(61000001, 3, 1)), Is.EqualTo(61000002));
            Assert.That(ChangeAttributesScrollHandler.RandomTableId(WithOption(61000001, 4, 1)), Is.EqualTo(61000003));
            Assert.That(ChangeAttributesScrollHandler.RandomTableId(WithOption(61000001, 6, 1)), Is.EqualTo(61000004));
            Assert.That(ChangeAttributesScrollHandler.RandomTableId(WithOption(61000001, 1, 1)), Is.Zero);
            Assert.That(ChangeAttributesScrollHandler.RandomTableId(WithOption(61000001, 3, 0)), Is.Zero);
            Assert.That(ChangeAttributesScrollHandler.RandomTableId(WithOption(11200001, 4, 0)), Is.EqualTo(61000019));
        });
    }

    private static readonly ItemOption Table = new(1f, new ItemOption.Range<int>(1, 4), [
        new ItemOption.Entry(BasicAttribute.Strength), new ItemOption.Entry(BasicAttribute.Dexterity),
        new ItemOption.Entry(BasicAttribute.Health), new ItemOption.Entry(BasicAttribute.Defense),
    ]);

    // The last check of the client's test (itemremakescrolldata.cpp:320-353), error 3 for each: no random options, an
    // option outside the item's random table, no table, and an armor with two exclusive options (FUN_1405f78f0);
    // an accessory may hold both. Before, none of these was checked.
    [Test]
    public void OptionTable_InvalidTargetStat() {
        const ChangeAttributesScrollError stat = ChangeAttributesScrollError.s_itemremake_scroll_error_invalid_target_stat;
        Assert.Multiple(() => {
            Assert.That(ChangeAttributesScrollHandler.OptionsFit(Rolled(11400001, BasicAttribute.Strength, BasicAttribute.Health), Table),
                Is.EqualTo(ChangeAttributesScrollError.none));
            Assert.That(ChangeAttributesScrollHandler.OptionsFit(Rolled(11400001), Table), Is.EqualTo(stat));
            Assert.That(ChangeAttributesScrollHandler.OptionsFit(Rolled(11400001, BasicAttribute.Luck), Table), Is.EqualTo(stat));
            Assert.That(ChangeAttributesScrollHandler.OptionsFit(Rolled(11400001, BasicAttribute.Health), null), Is.EqualTo(stat));
            Assert.That(ChangeAttributesScrollHandler.OptionsFit(Rolled(11400001, BasicAttribute.Strength, BasicAttribute.Dexterity), Table),
                Is.EqualTo(stat));
            Assert.That(ChangeAttributesScrollHandler.OptionsFit(Rolled(11200001, BasicAttribute.Strength, BasicAttribute.Dexterity), Table),
                Is.EqualTo(ChangeAttributesScrollError.none));
        });
    }
}
