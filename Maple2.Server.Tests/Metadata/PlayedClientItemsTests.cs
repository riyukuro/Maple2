using System.Collections.Generic;
using System.Linq;
using System.Xml;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;

namespace Maple2.Server.Tests.Metadata;

// MS2_PLAYED_CLIENT_XML: drop rows and reward items of items the played client has no item data for.
public class PlayedClientItemsTests {
    private static XmlDocument File(string items) {
        var document = new XmlDocument();
        document.LoadXml($"<ms2>{items}</ms2>");
        return document;
    }

    // The filter the ingest passes: here an environment with locale KR or feature Off is not available.
    private static bool Available(XmlElement node) => node.GetAttribute("locale") is "" or "NA" && node.GetAttribute("feature") != "Off";

    [Test]
    public void Ids_AreTheItemsWithAnAvailableEnvironment() {
        XmlDocument first = File("""
            <item id="100"><environment /></item>
            <item id="101"><environment locale="KR" /></item>
            <item id="102"><environment locale="KR" /><environment locale="NA" /></item>
            <item id="103"><environment feature="Off" /></item>
            <item id="104" />
            <item id="0"><environment /></item>
            <item id="x"><environment /></item>
            <other id="105"><environment /></other>
            """);
        XmlDocument second = File("""<item id="200"><environment /></item>""");

        Assert.That(PlayedClientItems.Ids([first, second], Available).OrderBy(id => id), Is.EqualTo(new[] { 100, 102, 200 }));
    }

    [Test]
    public void IsItemFile_IsTheItemDataFolder() {
        Assert.Multiple(() => {
            Assert.That(PlayedClientItems.IsItemFile("itemdata/100.xml"), Is.True);
            Assert.That(PlayedClientItems.IsItemFile("ItemData/100.xml"), Is.True);
            Assert.That(PlayedClientItems.IsItemFile("table/itemdata.xml"), Is.False);
        });
    }

    // A second item (the other gender's) must be known too; 0 names no item.
    [Test]
    public void Known_EachNamedItemMustBeKnown() {
        var known = new HashSet<int> { 1, 2 };
        Assert.Multiple(() => {
            Assert.That(PlayedClientItems.Known([1, 0], known), Is.True);
            Assert.That(PlayedClientItems.Known([1, 2], known), Is.True);
            Assert.That(PlayedClientItems.Known([1, 3], known), Is.False);
            Assert.That(PlayedClientItems.Known([3, 0], known), Is.False);
        });
    }

    private static IndividualDropItemTable.Item ServerRow(int id, int id2 = 0) =>
        new(Ids: [id, id2], Announce: false, ProperJobWeight: 0, ImproperJobWeight: 0, Weight: 1,
            DropCount: new IndividualDropItemTable.Range<int>(1, 1), Rarities: [], EnchantLevel: 0, SocketDataId: 0,
            DeductTradeCount: false, DeductRepackLimit: false, Bind: false, DisableBreak: false, MapIds: [], QuestId: 0);

    private static IndividualDropItemTable.Entry ServerGroup(int groupId, params IndividualDropItemTable.Item[] items) =>
        new(GroupId: groupId, SmartDropRate: 0, DropCounts: [new IndividualDropItemTable.Entry.DropCount(1, 100)],
            MinLevel: 0, ServerDrop: false, SmartGender: false, Items: items.ToList());

    // A box keeps its known rows; a group with none goes, and a box with no group left goes, so opening its item
    // fails before the box is used.
    [Test]
    public void ServerBoxes_DropUnknownRowsAndEmptiedGroupsAndBoxes() {
        var known = new HashSet<int> { 10, 11, 12 };
        var boxes = new Dictionary<int, IDictionary<int, IndividualDropItemTable.Entry>> {
            [1] = new Dictionary<int, IndividualDropItemTable.Entry> {
                [1] = ServerGroup(1, ServerRow(10), ServerRow(90), ServerRow(11, 91)),
                [2] = ServerGroup(2, ServerRow(92)),
            },
            [2] = new Dictionary<int, IndividualDropItemTable.Entry> {
                [1] = ServerGroup(1, ServerRow(93), ServerRow(94)),
            },
            [3] = new Dictionary<int, IndividualDropItemTable.Entry> {
                [5] = ServerGroup(5, ServerRow(12, 11)),
            },
        };

        Dictionary<int, IDictionary<int, IndividualDropItemTable.Entry>> kept = PlayedClientItems.Keep(boxes, known, out PlayedClientItems.Count count, out int boxesDropped);

        Assert.Multiple(() => {
            Assert.That(kept.Keys.OrderBy(id => id), Is.EqualTo(new[] { 1, 3 }));
            Assert.That(kept[1].Keys, Is.EqualTo(new[] { 1 }));
            Assert.That(kept[1][1].Items.Select(item => item.Ids[0]), Is.EqualTo(new[] { 10 }));
            Assert.That(kept[1][1].GroupId, Is.EqualTo(1));
            Assert.That(kept[3][5].Items.Single().Ids, Is.EqualTo(new[] { 12, 11 }));
            Assert.That(count, Is.EqualTo(new PlayedClientItems.Count(Dropped: 5, Kept: 2)));
            Assert.That(boxesDropped, Is.EqualTo(1));
            // The input is left as it was.
            Assert.That(boxes[1][1].Items, Has.Count.EqualTo(3));
        });
    }

    private static IndividualItemDropTable.Entry ClientRow(params int[] ids) =>
        new(ItemIds: ids, SmartGender: false, SmartDropRate: 0, Rarity: null, EnchantLevel: 0, ReduceTradeCount: false,
            ReduceRepackLimit: false, Bind: false, MinCount: 1, MaxCount: 1);

    [Test]
    public void ClientBoxes_DropUnknownRowsAndEmptiedGroupsAndBoxes() {
        var known = new HashSet<int> { 10, 11 };
        var boxes = new Dictionary<int, Dictionary<byte, IList<IndividualItemDropTable.Entry>>> {
            [1] = new() {
                [0] = [ClientRow(10), ClientRow(90)],
                [1] = [ClientRow(91)],
            },
            [2] = new() {
                [0] = [ClientRow(10, 92)],
            },
            [3] = new() {
                [2] = [ClientRow(11, 10)],
            },
        };

        Dictionary<int, Dictionary<byte, IList<IndividualItemDropTable.Entry>>> kept = PlayedClientItems.Keep(boxes, known, out PlayedClientItems.Count count, out int boxesDropped);

        Assert.Multiple(() => {
            Assert.That(kept.Keys.OrderBy(id => id), Is.EqualTo(new[] { 1, 3 }));
            Assert.That(kept[1].Keys, Is.EqualTo(new byte[] { 0 }));
            Assert.That(kept[1][0].Select(row => row.ItemIds[0]), Is.EqualTo(new[] { 10 }));
            Assert.That(kept[3][2].Single().ItemIds, Is.EqualTo(new[] { 11, 10 }));
            Assert.That(count, Is.EqualTo(new PlayedClientItems.Count(Dropped: 3, Kept: 2)));
            Assert.That(boxesDropped, Is.EqualTo(1));
        });
    }

    // A shop row goes when its item or its payment item is unknown; payment item 0 names none.
    [Test]
    public void ShopItems_NeedTheItemAndThePaymentItem() {
        var known = new HashSet<int> { 10, 20 };
        var count = new PlayedClientItems.Count();

        Assert.Multiple(() => {
            Assert.That(PlayedClientItems.KnownShopItem(10, 0, known, ref count), Is.True);
            Assert.That(PlayedClientItems.KnownShopItem(10, 20, known, ref count), Is.True);
            Assert.That(PlayedClientItems.KnownShopItem(90, 0, known, ref count), Is.False);
            Assert.That(PlayedClientItems.KnownShopItem(10, 91, known, ref count), Is.False);
            Assert.That(count, Is.EqualTo(new PlayedClientItems.Count(Dropped: 2, Kept: 2)));
        });
    }

    // An item the played client lacks is created by no server path; without the mark (no played client given, or
    // data ingested before the mark) every item can be.
    [Test]
    public void CanCreate_NotAnItemThePlayedClientLacks() {
        var property = new ItemMetadataProperty(false, 0, 100, 1, 13, string.Empty, string.Empty, ItemTag.None, 0, 0, 0, 0, 0, 0, 0, 0, 0, [], false, 0, false, [], [], [], 0, 0);
        var limit = new ItemMetadataLimit(Gender.All, 0, 0, 4, true, true, true, true, true, false, false, 0, [], []);
        var metadata = new ItemMetadata(10, "10", [], "", [], new ItemMetadataLife(0, 0), property, new ItemMetadataCustomize(0, 0), limit, null, null, [], null, null, null, null);

        Assert.Multiple(() => {
            Assert.That(PlayedClientItems.CanCreate(metadata), Is.True);
            Assert.That(PlayedClientItems.CanCreate(metadata with { Property = property with { NotInPlayedClient = true } }), Is.False);
        });
    }

    // Reward items of a level range: unknown ones go, the range stays even when none is left; counts add up.
    [Test]
    public void RewardItems_DropUnknownItemsAndAddToTheCount() {
        var known = new HashSet<int> { 10 };
        var count = new PlayedClientItems.Count(Dropped: 1, Kept: 1);

        RewardItem[] first = PlayedClientItems.Keep([new RewardItem(10, 1, 2), new RewardItem(90, 1, 1)], known, ref count);
        RewardItem[] second = PlayedClientItems.Keep([new RewardItem(91, 1, 1)], known, ref count);

        Assert.Multiple(() => {
            Assert.That(first.Select(item => (item.ItemId, item.Rarity, item.Amount)), Is.EqualTo(new[] { (10, (short) 1, 2) }));
            Assert.That(second, Is.Empty);
            Assert.That(count, Is.EqualTo(new PlayedClientItems.Count(Dropped: 3, Kept: 2)));
            Assert.That(count.ToString(), Is.EqualTo("3 rows dropped, 2 kept"));
        });
    }
}
