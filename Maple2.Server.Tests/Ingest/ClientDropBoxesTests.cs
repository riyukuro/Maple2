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
    // 4 has CN rows only and the client none for KR; 5 is not in the server table.
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
        </ms2>
        """;

    private static XmlDocument Doc(string xml) {
        var doc = new XmlDocument();
        doc.LoadXml(xml);
        return doc;
    }

    private static Dictionary<int, IndividualItemDrop> Read(out ClientDropBoxes.Counts counts) {
        XmlDocument server = Doc(Server);
        counts = ClientDropBoxes.Add(server, Doc(Client), ClientListedRows.Available);
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
            Assert.That(counts, Is.EqualTo(new ClientDropBoxes.Counts(2, 1, 2, 2)));
        });
    }
}
