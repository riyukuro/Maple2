using System.Collections.Generic;
using System.Xml;
using Maple2.File.Ingest.Helpers;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;

namespace Maple2.Server.Tests.Ingest;

// The box the played client's item data names for an item box (ClientItemBoxes): the item's OpenItemBox opens it
// when the data pack's function names another box or none. Shapes from the KR data.
public class ClientItemBoxesTests {
    // 30000928 Level Up Mission Box: the client's param1 30003 (a global box); 30000929 an individual box; 30000940 a
    // required amount; 30000950 two environments that disagree; 30000960 a box only for another locale; 30000970
    // another function.
    private const string Client = """
        <ms2>
          <item id="30000928"><environment><function name="OpenItemBox" param1="30003" /></environment></item>
          <item id="30000929"><environment><function name="OpenItemBox" param1="330001648" /></environment></item>
          <item id="30000940"><environment><function name="OpenItemBox" param1="330001700" param2="5" /></environment></item>
          <item id="30000950">
            <environment><function name="OpenItemBox" param1="330001701" /></environment>
            <environment><function name="OpenItemBox" param1="330001702" /></environment>
          </item>
          <item id="30000960">
            <environment locale="CN"><function name="OpenItemBox" param1="330001703" /></environment>
            <environment><function name="OpenItemBox" param1="330001704" /></environment>
          </item>
          <item id="30000970"><environment><function name="SelectItemBox" parameter="1,330001705" /></environment></item>
        </ms2>
        """;

    private static Dictionary<int, ClientItemBoxes.Box> Read() {
        var doc = new XmlDocument();
        doc.LoadXml(Client);
        return ClientItemBoxes.Read([doc], element => element.GetAttribute("locale") is "" or "KR");
    }

    [Test]
    public void Read_TakesTheAvailableBoxTheEnvironmentsAgreeOn() {
        Assert.That(Read(), Is.EqualTo(new Dictionary<int, ClientItemBoxes.Box> {
            [30000928] = new(30003, 0),
            [30000929] = new(330001648, 0),
            [30000940] = new(330001700, 5),
            [30000960] = new(330001704, 0),
        }));
    }

    // The data pack's function opens the client's box already (global or individual place, comma or xml form), or not.
    [TestCase("OpenItemBox", "0,1,30000301,0", 30003, false)]
    [TestCase("OpenItemBox", "53,915,30000201,0", 330000420, false)]
    [TestCase("OpenItemBox", "0,1,0,330001854", 330001418, false)]
    [TestCase("OpenItemBox", "0,1,0,330001648", 330001648, true)]
    [TestCase("OpenItemBox", "30003,1,0,0", 30003, true)]
    [TestCase("OpenItemBox", "<v globalDropBoxId=\"0\" individualDropBoxId=\"330002090\" />", 330002090, true)]
    [TestCase("SelectItemBox", "1,330002718", 320000362, false)]
    [TestCase("SurvivalLevelExp", "<v SurvivalExp=\"100000\" />", 320000546, false)]
    public void Names_TheBoxThePackAlreadyOpens(string name, string parameter, int box, bool names) {
        Assert.That(ClientItemBoxes.Names(name, parameter, box), Is.EqualTo(names));
    }

    // A global box in OpenItemBox's global place, any other in its individual place; a required amount after them.
    [TestCase(30003, 0, true, "30003,1,0,0")]
    [TestCase(330001648, 0, false, "0,1,0,330001648")]
    [TestCase(330001700, 5, false, "0,1,0,330001700,5")]
    public void Parameter_PutsTheBoxInItsPlace(int box, int amount, bool global, string parameter) {
        Assert.That(ClientItemBoxes.Parameter(new ClientItemBoxes.Box(box, amount), global), Is.EqualTo(parameter));
    }

    private static ItemMetadata Item(string name, int slotMax, string category, int level, TransferType transfer, long sell, long duration = 0, int type = 10,
            int tradeMaxRarity = 4, int tradableCount = 0, long customSell = 0) {
        var limit = new ItemMetadataLimit(Gender.All, level, transfer, tradeMaxRarity, false, false, true, false, false, false, false, 0, [], []);
        var property = new ItemMetadataProperty(false, 0, slotMax, type, 0, category, string.Empty, ItemTag.None, 0, 0, 0, 0, 0, tradableCount, 0, 0, 0, [], false, 0, false, [],
            [sell, sell, sell, sell, sell, sell], [customSell, 0, 0, 0, 0, 0], 0, 0);
        var function = new ItemMetadataFunction(ItemFunction.OpenItemBox, nameof(ItemFunction.OpenItemBox), "0,1,0,320000547", false);
        return new ItemMetadata(20301431, name, [], "", [], new ItemMetadataLife(duration, 0), property, new ItemMetadataCustomize(0, 0), limit, null, function, [], null, null, null, null);
    }

    // 20301431: a mini-game ticket in the pack (stack 9999, ETC, level 1, account bound), the release client's
    // weapon box (stack 999, Package, no level, transfer type 1): the client's name, stack, category, limits and sell
    // prices; the pack's type (the bag tab rule decides that) and function (already the client's box).
    [Test]
    public void ClientFields_TakeTheClientsRow() {
        // The pack's server-only fields (trade count, trade rarity, custom price) stay; a client row has defaults there.
        ItemMetadata pack = Item("Mini-Game: Survival M", 9999, "ETC", 10, TransferType.BindOnLoot, 0, tradeMaxRarity: 3, tradableCount: 2, customSell: 241);
        ItemMetadata client = Item("Halloween Weapon Box", 999, "Package", 1, TransferType.Untradeable, 482, 86400, type: 25, tradeMaxRarity: 0);
        ItemMetadata taken = ClientItemBoxes.ClientFields(pack, client);
        Assert.Multiple(() => {
            Assert.That(taken.Name, Is.EqualTo("Halloween Weapon Box"));
            Assert.That((taken.Property.SlotMax, taken.Property.Category, taken.Property.Type), Is.EqualTo((999, "Package", 10)));
            Assert.That((taken.Limit.Level, taken.Limit.TransferType), Is.EqualTo((1, TransferType.Untradeable)));
            Assert.That((taken.Limit.TradeMaxRarity, taken.Property.TradableCount, taken.Property.CustomSellPrices[0]), Is.EqualTo((3, 2, 241L)));
            Assert.That(taken.Property.SellPrices, Is.EqualTo(new long[] { 482, 482, 482, 482, 482, 482 }));
            Assert.That(taken.Life.ExpirationDuration, Is.EqualTo(86400));
            Assert.That(taken.Function, Is.SameAs(pack.Function));
        });
    }

    // A client row with the pack's values (a mission box the client did not reuse) leaves the row as it is; a client
    // item without a name keeps the pack's name.
    [Test]
    public void ClientFields_SameRowStays() {
        ItemMetadata pack = Item("Level Up Mission Box", 9999, "Package", 1, TransferType.BindOnLoot, 0);
        Assert.That(ClientItemBoxes.ClientFields(pack, Item("Level Up Mission Box", 9999, "Package", 1, TransferType.BindOnLoot, 0)), Is.SameAs(pack));
        Assert.That(ClientItemBoxes.ClientFields(pack, Item("", 9999, "Package", 1, TransferType.BindOnLoot, 0)), Is.SameAs(pack));
        // A client row without a sell list (the pack's lists are zeros) has the same prices.
        ItemMetadata noList = Item("Level Up Mission Box", 9999, "Package", 1, TransferType.BindOnLoot, 0);
        noList = noList with { Property = noList.Property with { SellPrices = [], CustomSellPrices = [] } };
        Assert.That(ClientItemBoxes.ClientFields(pack, noList), Is.SameAs(pack));
    }
}
