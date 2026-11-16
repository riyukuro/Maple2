using System.Collections.Generic;
using System.Linq;
using System.Xml;
using M2dXmlGenerator;
using Maple2.File.Ingest.Helpers;
using IndividualItemDrop = Maple2.File.Parser.Xml.Table.Server.IndividualItemDrop;

namespace Maple2.Server.Tests.Ingest;

// LOCALE=KR: a drop box the server's table holds with NA or CN rows only is taken from the played client's table.
// Synthetic boxes.
[NonParallelizable]
public class ClientDropBoxesTests {
    private string oldLocale = null!;

    [SetUp]
    public void SetLocale() {
        oldLocale = FeatureLocaleFilter.Locale;
        FeatureLocaleFilter.Locale = "KR";
    }

    [TearDown]
    public void RestoreLocale() {
        FeatureLocaleFilter.Locale = oldLocale;
    }

    // 1 has a KR row; 2 has NA rows that match the client's; 3 has NA rows that differ from the client's in group 2;
    // 4 has CN rows only and the client none for KR; 5 is not in the server table; 6 is an item box with an NA group and
    // two KR groups, one with a drop chance holding the client's group 2 under id 3 and one the client lacks, where the
    // client lists three KR groups (the adventure bonus box 330001968 shape); 7 has as many KR groups as the client; 8 is box 6's shape that no item opens
    // (a monster box); 9 is an item box whose KR group has 2 of the client group's 3 rows (330001417); 10 has NA rows with
    // other counts than the client's (the prestige reward box 330002623 shape).
    private const string Server = """
        <ms2>
          <dropBox dropBoxID="1" comment="server">
            <group dropGroupID="1" dropCount="1">
              <v itemID="10000001" minCount="1" maxCount="1" weight="7" />
            </group>
          </dropBox>
          <dropBox dropBoxID="2" comment="na box">
            <group dropGroupID="1" dropCount="2" dropCountProbability="100" locale="NA">
              <v itemID="20000001" minCount="1" maxCount="1" weight="30" isAnnounce="1" />
              <v itemID="20000002" minCount="1" maxCount="1" weight="70" />
              <v itemID="20000003" minCount="1" maxCount="1" weight="5" />
            </group>
          </dropBox>
          <dropBox dropBoxID="3">
            <group dropGroupID="1" locale="NA">
              <v itemID="30000001" minCount="1" maxCount="1" weight="40" />
            </group>
            <group dropGroupID="2" smartDropRate="100" locale="NA">
              <v itemID="30000002" minCount="1" maxCount="1" properJobWeight="9" />
            </group>
          </dropBox>
          <dropBox dropBoxID="4">
            <group dropGroupID="1" locale="CN">
              <v itemID="40000001" minCount="1" maxCount="1" weight="1" />
            </group>
          </dropBox>
          <dropBox dropBoxID="6">
            <group dropGroupID="1" smartDropRate="100">
              <v itemID="60000009" minCount="1" maxCount="1" weight="10000" locale="NA" />
            </group>
            <group dropGroupID="3" smartDropRate="100" dropCount="0,1" dropCountProbability="9950,50">
              <v itemID="60000002" minCount="3" maxCount="3" weight="10000" locale="KR" />
            </group>
            <group dropGroupID="5">
              <v itemID="60000005" minCount="1" maxCount="1" weight="10000" locale="KR" />
            </group>
          </dropBox>
          <dropBox dropBoxID="7">
            <group dropGroupID="1">
              <v itemID="70000001" minCount="1" maxCount="1" weight="5" />
            </group>
            <group dropGroupID="2">
              <v itemID="70000002" minCount="1" maxCount="1" weight="5" />
            </group>
          </dropBox>
          <dropBox dropBoxID="8">
            <group dropGroupID="1">
              <v itemID="80000009" minCount="1" maxCount="1" weight="10000" locale="NA" />
            </group>
            <group dropGroupID="3" dropCount="0,1" dropCountProbability="9950,50">
              <v itemID="80000002" minCount="1" maxCount="1" weight="10000" locale="KR" />
            </group>
          </dropBox>
          <dropBox dropBoxID="9">
            <group dropGroupID="2">
              <v itemID="90000001" minCount="2" maxCount="2" weight="60" locale="KR" />
              <v itemID="90000002" minCount="2" maxCount="2" weight="40" locale="KR" />
            </group>
          </dropBox>
          <dropBox dropBoxID="10">
            <group dropGroupID="1">
              <v itemID="10000101" minCount="4800" maxCount="4800" weight="10000" locale="NA" />
            </group>
            <group dropGroupID="2">
              <v itemID="10000102" minCount="10" maxCount="10" weight="10000" locale="NA" />
            </group>
            <group dropGroupID="3">
              <v itemID="10000103" minCount="15" maxCount="15" weight="10000" locale="NA" />
            </group>
          </dropBox>
        </ms2>
        """;

    private const string Client = """
        <ms2>
          <dropBox dropBoxID="1">
            <group dropGroupID="1">
              <v itemID="10000009" minCount="1" maxCount="1" />
            </group>
          </dropBox>
          <dropBox dropBoxID="2">
            <group dropGroupID="1">
              <v itemID="20000001" minCount="3" maxCount="3" locale="KR" />
              <v itemID="20000002" minCount="1" maxCount="1" />
              <v itemID="20000003" minCount="2" maxCount="2" />
              <v itemID="20000009" minCount="1" maxCount="1" locale="CN" />
            </group>
          </dropBox>
          <dropBox dropBoxID="3">
            <group dropGroupID="1">
              <v itemID="30000001" minCount="1" maxCount="1" />
            </group>
            <group dropGroupID="2" smartDropRate="100">
              <v itemID="30000002" minCount="1" maxCount="1" />
              <v itemID="30000003" minCount="1" maxCount="1" />
            </group>
          </dropBox>
          <dropBox dropBoxID="4">
            <group dropGroupID="1" locale="CN">
              <v itemID="40000002" minCount="1" maxCount="1" />
            </group>
          </dropBox>
          <dropBox dropBoxID="5">
            <group dropGroupID="1">
              <v itemID="50000001" minCount="1" maxCount="1" />
            </group>
          </dropBox>
          <dropBox dropBoxID="6">
            <group dropGroupID="1" smartDropRate="1">
              <v itemID="60000001" />
            </group>
            <group dropGroupID="2" smartDropRate="1">
              <v itemID="60000002" minCount="3" locale="KR" />
              <v itemID="60000002" minCount="12" locale="CN" />
            </group>
            <group dropGroupID="3" smartDropRate="1">
              <v itemID="60000003" minCount="53" />
            </group>
            <group dropGroupID="4" locale="CN" smartDropRate="1">
              <v itemID="60000004" />
            </group>
          </dropBox>
          <dropBox dropBoxID="7">
            <group dropGroupID="1">
              <v itemID="70000009" />
            </group>
            <group dropGroupID="2">
              <v itemID="70000008" />
            </group>
          </dropBox>
          <dropBox dropBoxID="8">
            <group dropGroupID="1">
              <v itemID="80000001" />
            </group>
            <group dropGroupID="2">
              <v itemID="80000002" />
            </group>
            <group dropGroupID="3">
              <v itemID="80000003" />
            </group>
          </dropBox>
          <dropBox dropBoxID="9">
            <group dropGroupID="1">
              <v itemID="90000009" minCount="30" />
            </group>
            <group dropGroupID="2">
              <v itemID="90000001" />
              <v itemID="90000002" />
              <v itemID="90000003" />
            </group>
          </dropBox>
          <dropBox dropBoxID="10">
            <group dropGroupID="1">
              <v itemID="10000101" minCount="2550" />
            </group>
            <group dropGroupID="2">
              <v itemID="10000102" minCount="2" />
            </group>
            <group dropGroupID="3">
              <v itemID="10000103" minCount="5" maxCount="9" />
            </group>
            <group dropGroupID="4">
              <v itemID="10000104" />
            </group>
          </dropBox>
        </ms2>
        """;

    private static XmlDocument Doc(string xml) {
        var doc = new XmlDocument();
        doc.LoadXml(xml);
        return doc;
    }

    private static Dictionary<int, IndividualItemDrop> Read(out ClientDropBoxes.Counts counts) {
        XmlDocument server = Doc(Server);
        counts = ClientDropBoxes.Add(server, Doc(Client), ClientListedRows.Available, id => id is "6" or "9");
        return ServerDropTable.Read(new XmlNodeReader(server)).ToDictionary(box => box.Id, box => box.Box);
    }

    [Test]
    public void BoxWithServerRows_KeepsTheServerRows() {
        IndividualItemDrop box = Read(out _)[1];
        Assert.That(box.group.Single().v.Single().itemID, Is.EqualTo(10000001));
        Assert.That(box.group.Single().v.Single().weight, Is.EqualTo(7));
    }

    [Test]
    public void MatchingGroup_TakesTheServerWeights() {
        IndividualItemDrop box = Read(out _)[2];
        IndividualItemDrop.Group group = box.group.Single();
        Assert.Multiple(() => {
            Assert.That(box.comment, Is.EqualTo("na box"));
            Assert.That(group.dropCount, Is.EqualTo(new[] { 2 }));
            Assert.That(group.v.Select(v => (v.itemID, v.minCount, v.weight)), Is.EqualTo(new[] {
                (20000001, 3, 30), (20000002, 1, 70), (20000003, 2, 5),
            }));
            Assert.That(group.v[0].isAnnounce, Is.True);
        });
    }

    [Test]
    public void OtherGroup_KeepsTheClientRowsWithoutWeights() {
        IndividualItemDrop box = Read(out _)[3];
        Assert.Multiple(() => {
            Assert.That(box.group[0].v.Single().weight, Is.EqualTo(40));
            Assert.That(box.group[1].smartDropRate, Is.EqualTo(100));
            Assert.That(box.group[1].v.Select(v => (v.itemID, v.properJobWeight)), Is.EqualTo(new[] { (30000002, 0), (30000003, 0) }));
        });
    }

    [Test]
    public void ClientWithoutRows_LeavesTheBoxEmpty_ClientOnlyBoxIsAdded() {
        Dictionary<int, IndividualItemDrop> boxes = Read(out ClientDropBoxes.Counts counts);
        Assert.Multiple(() => {
            Assert.That(boxes[4].group, Is.Empty);
            Assert.That(boxes[5].group.Single().v.Single().itemID, Is.EqualTo(50000001));
            Assert.That(counts, Is.EqualTo(new ClientDropBoxes.Counts(3, 1, 5, 6, 2, 3, 1, 1)));
        });
    }

    // An item box whose KR rows cover fewer groups than the client's KR rows gets the client's KR groups that no server
    // group holds, matched by rows, not by id: the server's group 3 is the client's group 2 (kept with its drop
    // chance), the client's group 1 takes the place of the server's NA group 1, and the client's group 3 gets a free
    // id, as the server's group 3 is taken. The server's group 5, which the client lacks, stays; the CN group and the
    // CN row are left out. Each stack once. Before, the box gave 60000002 x3 alone.
    [Test]
    public void ItemBox_FewerAvailableGroupsThanTheClient_AddsTheClientGroups() {
        List<IndividualItemDrop.Group> groups = Read(out _)[6].group.Where(group => group.v.Count > 0).ToList();
        Assert.Multiple(() => {
            Assert.That(groups.Select(group => (group.dropGroupID, group.v.Select(v => (v.itemID, v.minCount)).Single())),
                Is.EqualTo(new[] { (1, (60000001, 1)), (3, (60000002, 3)), (5, (60000005, 1)), (6, (60000003, 53)) }));
            Assert.That(groups[1].dropCount, Is.EqualTo(new[] { 0, 1 }));
            Assert.That(groups[1].v.Single().weight, Is.EqualTo(10000));
        });
    }

    // A client group that shares items with a kept server group is another version of it: the server's group, with its
    // weights, stays alone (no second pick of 90000001); the client's other group is added.
    [Test]
    public void ItemBox_ClientGroupSharingItems_KeepsTheServerGroupAlone() {
        List<IndividualItemDrop.Group> groups = Read(out _)[9].group.Where(group => group.v.Count > 0).ToList();
        Assert.That(groups.Select(group => (group.dropGroupID, group.v.Select(v => (v.itemID, v.weight)).ToArray())),
            Is.EqualTo(new[] { (2, new[] { (90000001, 60), (90000002, 40) }), (1, new[] { (90000009, 0) }) }));
    }

    // A box taken from the client's copy drops the client's counts, read as the client reads them (minCount 1 when
    // absent, maxCount at least minCount), with the weights of the matching NA rows. Before, the NA row's maxCount was
    // copied: 2550 became a 2550-4800 range, 2 a 2-10 range.
    [Test]
    public void ClientBox_KeepsTheClientCounts() {
        IndividualItemDrop box = Read(out _)[10];
        Assert.That(box.group.Select(group => group.v.Select(v => (v.itemID, v.minCount, v.maxCount, v.weight)).Single()),
            Is.EqualTo(new[] { (10000101, 2550, 2550, 10000), (10000102, 2, 2, 10000), (10000103, 5, 9, 10000), (10000104, 1, 1, 0) }));
    }

    // The same shape in a box no item opens (a monster box) keeps the server's rows only: the client's rows carry no
    // drop chances, so their groups would drop on every kill.
    [Test]
    public void FewerAvailableGroups_NotAnItemBox_KeepsTheServerRows() {
        List<IndividualItemDrop.Group> groups = Read(out _)[8].group.Where(group => group.v.Count > 0).ToList();
        Assert.Multiple(() => {
            Assert.That(groups.Select(group => group.v.Single().itemID), Is.EqualTo(new[] { 80000002 }));
            Assert.That(groups.Single().dropCount, Is.EqualTo(new[] { 0, 1 }));
        });
    }

    // The box ids item functions name, in ItemBoxManager's places.
    [TestCase("OpenItemBox", "0,1,0,330001968", "", new[] { "330001968" })]
    [TestCase("OpenItemBox", "0,1,30000303,0", "", new string[0])]
    [TestCase("OpenItemBox", "", "30005", new[] { "30005" })]
    [TestCase("OpenItemBox", "<v individualDropBoxId=\"330001542\" additionalEffectId=\"90000654\" />", "", new[] { "330001542" })]
    [TestCase("SelectItemBox", "1,330001859", "", new[] { "330001859" })]
    [TestCase("OpenItemBoxWithKey", "30001041,1,0,0,0,330001839", "", new[] { "330001839" })]
    [TestCase("OpenItemBoxLimitedQuantity", "< v individualDropBoxId=\"340000326\" />", "", new[] { "340000326" })]
    [TestCase("OpenItemBoxLullu", "<v commonBoxId=\"330000961\" unCommonBoxId=\"330000962\" />", "", new[] { "330000961", "330000962" })]
    [TestCase("OpenPetBox", "<v itemTag=\"PetBoxKey\" commonBoxId=\"330000101\" unCommonBoxId=\"330000102,330000103\" />", "", new[] { "330000101", "330000102", "330000103" })]
    [TestCase("OpenGachaBox", "1001001,22101001", "", new string[0])]
    public void ItemBoxDropIds_NamesTheBoxes(string name, string parameter, string param1, string[] ids) {
        Assert.That(ItemBoxDropIds.Of(name, parameter, param1), Is.EqualTo(ids));
    }

    // The played client's param1 of a mission box names global box 30005, not an individual box of that id.
    [TestCase("30005", new string[0])]
    [TestCase("330001648", new[] { "330001648" })]
    public void ItemBoxDropIds_SkipsGlobalBoxes(string param1, string[] ids) {
        Assert.That(ItemBoxDropIds.Of("OpenItemBox", "", param1, new HashSet<int> { 30001, 30003, 30005 }), Is.EqualTo(ids));
    }

    // A box with as many KR groups as the client keeps the server's rows, though the items differ.
    [Test]
    public void AsManyAvailableGroupsAsTheClient_KeepsTheServerRows() {
        IndividualItemDrop box = Read(out _)[7];
        Assert.That(box.group.Select(group => (group.v.Single().itemID, group.v.Single().weight)),
            Is.EqualTo(new[] { (70000001, 5), (70000002, 5) }));
    }
}
