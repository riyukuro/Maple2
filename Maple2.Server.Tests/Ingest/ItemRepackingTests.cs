using System.Collections.Generic;
using System.Xml;
using Maple2.File.Ingest.Helpers;

namespace Maple2.Server.Tests.Ingest;

// The repack scroll ids the client reads (ItemProperty::Load): the item's own list, and the global list appended
// after it only under the GlobalTransferType feature.
public class ItemRepackingTests {
    [Test]
    public void OwnList_WithoutTheFeature() {
        Assert.That(ItemRepacking.ScrollIds([20300800], [20300900], false), Is.EqualTo(new[] { 20300800 }));
        Assert.That(ItemRepacking.ScrollIds(null, [20300900], false), Is.Empty);
    }

    [Test]
    public void GlobalList_AppendedUnderTheFeature() {
        Assert.That(ItemRepacking.ScrollIds([20300800], [20300900, 20300901], true), Is.EqualTo(new[] { 20300800, 20300900, 20300901 }));
        Assert.That(ItemRepacking.ScrollIds(null, [20300900], true), Is.EqualTo(new[] { 20300900 }));
        Assert.That(ItemRepacking.ScrollIds([20300800], [], true), Is.EqualTo(new[] { 20300800 }));
        Assert.That(ItemRepacking.ScrollIds([20300800], null, true), Is.EqualTo(new[] { 20300800 }));
    }

    // The played client's lists (KR shapes): 11200072 names its own list, 61020002 none, 61020079 a list in both
    // places, 61020080 two environments that disagree.
    private const string Client = """
        <ms2>
          <item id="11200072"><environment><property rePackingLimitCount="1" rePackingScrollID="10000001,10000003" /></environment></item>
          <item id="61020002"><environment><property slotMax="1" /></environment></item>
          <item id="61020079"><environment><property rePackingScrollID="10000002" globalRePackingScrollID="10000005" /></environment></item>
          <item id="61020080">
            <environment><property rePackingScrollID="10000002" /></environment>
            <environment><property rePackingScrollID="10000003" /></environment>
          </item>
        </ms2>
        """;

    [TestCase(false, 10000002)]
    [TestCase(true, 10000002, 10000005)]
    public void Played_TheClientsListByItem(bool globalTransferType, params int[] list61020079) {
        var doc = new XmlDocument();
        doc.LoadXml(Client);
        Dictionary<int, int[]> lists = ItemRepacking.Played([doc], _ => true, globalTransferType);
        Assert.That(lists, Is.EqualTo(new Dictionary<int, int[]> {
            [11200072] = [10000001, 10000003],
            [61020002] = [],
            [61020079] = list61020079,
        }));
    }
}
