using System.Linq;
using System.Xml;
using Maple2.File.Ingest.Utils;
using Maple2.File.Parser.Xml.Item;

namespace Maple2.Server.Tests.Ingest;

// An item only the played client has, read from its itemdata layout (model in itemmodel, ids in property param1,
// box ids in function param1/param2). Synthetic values.
public class PlayedItemDataTests {
    private static XmlElement Element(string xml, string name) {
        var document = new XmlDocument();
        document.LoadXml(xml);
        return (XmlElement) document.DocumentElement!.SelectSingleNode(name)!;
    }

    private static XmlElement Environment(string xml) => Element(xml, "environment");

    [Test]
    public void ReadsGearWithItsModel() {
        XmlElement model = Element(
            "<ms2><ItemModel id=\"500\"><slots><slot name=\"CP\"><asset name=\"A.nif\" gender=\"0\" /><scale value=\"1\" /></slot></slots>" +
            "<customize colorPalette=\"10\" defaultColorIndex=\"2\" /></ItemModel></ms2>", "ItemModel");
        ItemData data = PlayedItemData.Read(Environment(
            "<item id=\"1\"><environment><basic originID=\"7\" stringTag=\"T\" />" +
            "<property rank=\"4\" type=\"1\" subtype=\"80\" skinType=\"2\" slotMax=\"5\" param1=\"99\"><sell price=\"40\" /></property>" +
            "<limit genderLimit=\"0\" levelLimit=\"50\" transferType=\"1\" />" +
            "<option constantID=\"123\" randomID=\"456\" optionLevelFactor=\"60\" />" +
            "<tool itemPreset=\"500\" /></environment></item>"), model);

        Assert.Multiple(() => {
            Assert.That(data.basic.originID, Is.EqualTo(7));
            Assert.That(data.basic.stringTag, Is.EqualTo("T"));
            Assert.That(data.property.rank, Is.EqualTo(4));
            Assert.That(data.property.type, Is.EqualTo(1));
            Assert.That(data.property.subtype, Is.EqualTo(80));
            Assert.That(data.property.skinType, Is.EqualTo(2));
            Assert.That(data.property.skin, Is.True);
            Assert.That(data.property.slotMax, Is.EqualTo(5));
            Assert.That(data.property.sell.price, Is.EqualTo(new long[] { 40, 40, 40, 40, 40, 40 }));
            Assert.That(data.limit.levelLimit, Is.EqualTo(50));
            Assert.That(data.limit.transferType, Is.EqualTo(1));
            // Type 1 subtype 80: param1 is the weapon skill.
            Assert.That(data.objectWeaponSkill.skillID, Is.EqualTo(99));
            Assert.That(data.skill.skillID, Is.EqualTo(0));
            // The client's option rows are left out.
            Assert.That(data.option.constant, Is.EqualTo(0));
            Assert.That(data.option.random, Is.EqualTo(0));
            // Slots and customize come from the model; a scale without a range is 0 to 1.
            Assert.That(data.slots.slot.Select(slot => slot.name), Is.EqualTo(new[] { "CP" }));
            Assert.That(data.slots.slot[0].scale[0].min, Is.EqualTo(0f));
            Assert.That(data.slots.slot[0].scale[0].max, Is.EqualTo(1f));
            Assert.That(data.customize.colorPalette, Is.EqualTo(10));
            Assert.That(data.customize.defaultColorIndex, Is.EqualTo(2));
        });
    }

    [Test]
    public void ColorListKeepsTheFirstColor() {
        XmlElement model = Element(
            "<ms2><ItemModel id=\"500\"><customize color=\"1\" colorPalette=\"10\" defaultColorIndex=\"19,3\" colorDelay=\"500,500\" /></ItemModel></ms2>",
            "ItemModel");
        ItemData data = PlayedItemData.Read(Environment(
            "<item id=\"1\"><environment><property type=\"1\" /><tool itemPreset=\"500\" /></environment></item>"), model);

        Assert.That(data.customize.defaultColorIndex, Is.EqualTo(19));
    }

    [Test]
    public void LeftOutAttributesAreTheClientDefaults() {
        ItemData data = PlayedItemData.Read(Environment(
            "<item id=\"1\"><environment><property type=\"25\" skinType=\"0\" /></environment></item>"), null);

        Assert.Multiple(() => {
            Assert.That(data.property.skin, Is.False);
            Assert.That(data.property.slotMax, Is.EqualTo(1));
            Assert.That(data.property.itemGroup, Is.EqualTo(0));
            Assert.That(data.limit.levelLimit, Is.EqualTo(1));
            Assert.That((int) data.limit.genderLimit, Is.EqualTo(2));
            Assert.That(data.customize.defaultColorIndex, Is.EqualTo(-1));
            Assert.That(data.function.name, Is.Empty);
            Assert.That(data.slots.slot, Is.Empty);
        });
    }

    [Test]
    public void BoxIdsBecomeTheServerParameter() {
        ItemData one = PlayedItemData.Read(Environment(
            "<item id=\"1\"><environment><property type=\"25\" /><function name=\"OpenItemBox\" param1=\"510\" /></environment></item>"), null);
        ItemData two = PlayedItemData.Read(Environment(
            "<item id=\"1\"><environment><property type=\"25\" /><function name=\"OpenItemBox\" param1=\"510\" param2=\"3\" /></environment></item>"), null);
        ItemData instrument = PlayedItemData.Read(Environment(
            "<item id=\"1\"><environment><property type=\"30\" param1=\"8\" /><function name=\"OpenInstrument\" /></environment></item>"), null);
        ItemData given = PlayedItemData.Read(Environment(
            "<item id=\"1\"><environment><property type=\"20\" /><function name=\"SelectItemBox\" parameter=\"1,2\" /></environment></item>"), null);

        Assert.Multiple(() => {
            Assert.That(one.function.name, Is.EqualTo("OpenItemBox"));
            Assert.That(one.function.parameter, Is.EqualTo("0,1,0,510"));
            Assert.That(two.function.parameter, Is.EqualTo("0,1,0,510,3"));
            Assert.That(instrument.function.parameter, Is.EqualTo("8"));
            Assert.That(given.function.parameter, Is.EqualTo("1,2"));
        });
    }

    [TestCase(9, "ride")]
    [TestCase(11, "pet")]
    [TestCase(20, "shop")]
    public void Param1NamesTheIdOfTheType(int type, string target) {
        ItemData data = PlayedItemData.Read(Environment(
            $"<item id=\"1\"><environment><property type=\"{type}\" param1=\"77\" /></environment></item>"), null);

        int value = target switch {
            "ride" => data.ride.rideMonster,
            "pet" => data.pet.petID,
            _ => data.Shop?.systemShopID ?? 0,
        };
        Assert.That(value, Is.EqualTo(77));
    }

    [Test]
    public void Param1DoesNotReplaceANamedId() {
        ItemData data = PlayedItemData.Read(Environment(
            "<item id=\"1\"><environment><property type=\"11\" param1=\"77\" /><pet petID=\"5\" /></environment></item>"), null);

        Assert.That(data.pet.petID, Is.EqualTo(5));
    }

    [Test]
    public void Param1IsNotASkillOrAnyWeaponSkill() {
        // Type 5: param1 is the glide id; type 1 outside subtypes 80 to 83: nothing.
        ItemData glide = PlayedItemData.Read(Environment(
            "<item id=\"1\"><environment><property type=\"5\" param1=\"77\" /></environment></item>"), null);
        ItemData gear = PlayedItemData.Read(Environment(
            "<item id=\"1\"><environment><property type=\"1\" subtype=\"13\" param1=\"77\" /></environment></item>"), null);

        Assert.Multiple(() => {
            Assert.That(glide.skill.skillID, Is.EqualTo(0));
            Assert.That(gear.objectWeaponSkill.skillID, Is.EqualTo(0));
        });
    }

    [Test]
    public void ClientOptionRowsAreCounted() {
        Assert.Multiple(() => {
            Assert.That(PlayedItemData.HasClientOption(Environment(
                "<item id=\"1\"><environment><option constantID=\"3\" /></environment></item>")), Is.True);
            Assert.That(PlayedItemData.HasClientOption(Environment(
                "<item id=\"1\"><environment><option optionLevelFactor=\"3\" /></environment></item>")), Is.False);
            Assert.That(PlayedItemData.HasClientOption(Environment(
                "<item id=\"1\"><environment /></item>")), Is.False);
        });
    }
}
