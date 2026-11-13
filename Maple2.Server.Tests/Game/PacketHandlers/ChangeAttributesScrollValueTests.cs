using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.PacketHandlers;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The remake scroll's row (ChangeAttributesScrollHandler.ScrollFits): the client refuses a row without an option
// value (addOpValue 0) as invalid scroll data (error 11) once the item fits its types, rarities and levels.
public class ChangeAttributesScrollValueTests {
    private static Item Accessory() => new(TestGameSession.Metadata(11200001), rarity: 4);

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

    // The item checks come first, as in the client.
    [Test]
    public void NoValue_WrongRarity_ImpossibleItem() {
        Item item = new(TestGameSession.Metadata(11200001), rarity: 3);
        Assert.That(ChangeAttributesScrollHandler.ScrollFits(item, Row(item, RollValueType.None)),
            Is.EqualTo(ChangeAttributesScrollError.s_itemremake_scroll_error_impossible_item));
    }
}
