using System;
using System.IO;
using System.Linq;
using System.Xml;
using M2dXmlGenerator;
using Maple2.File.Ingest.Helpers;

namespace Maple2.Server.Tests.Ingest;

// MS2_ORIGINAL_DROP_TABLE: the ingest reads the server pack's original (KMS) drop table instead of today's.
public class ServerDropTableTests {
    [TestCase(null, false)]
    [TestCase("", false)]
    [TestCase("0", false)]
    [TestCase("false", false)]
    [TestCase("1", true)]
    [TestCase("true", true)]
    [TestCase(" TRUE ", true)]
    public void UseOriginal_DefaultOff(string? value, bool expected) {
        Assert.That(ServerDropTable.UseOriginal(value), Is.EqualTo(expected));
    }

    // A typo stops the ingest instead of quietly reading today's table.
    [TestCase("yes")]
    [TestCase("2")]
    public void UseOriginal_OtherValueThrows(string value) {
        Assert.Throws<ArgumentException>(() => ServerDropTable.UseOriginal(value));
    }

    [Test]
    public void FileName_IsTheBackupOnlyWhenOn() {
        Assert.Multiple(() => {
            Assert.That(ServerDropTable.FileName(false), Is.EqualTo("table/Server/individualItemDrop_Final.xml"));
            Assert.That(ServerDropTable.FileName(true), Is.EqualTo("table/Server/individualItemDrop_Final_Backup.xml"));
        });
    }

    // Both files share one layout, so the read keeps the fields and the existing locale filter: a row for the
    // locale replaces the no-locale row with the same item id, and a row for another locale is left out.
    [Test]
    public void Read_KeepsFieldsAndLocaleFilter() {
        const string xml = """
            <ms2>
              <dropBox dropBoxID="10000" comment="c" probVer="1">
                <group dropGroupID="1" dropCount="1,2" dropCountProbability="70,30" smartDropRate="100">
                  <v itemID="20000001" minCount="1" maxCount="3" grade="1,2" gradeProbability="60,40" weight="500" />
                  <v itemID="20000002" minCount="2" maxCount="2" weight="100" />
                  <v itemID="20000002" minCount="5" maxCount="5" weight="100" locale="KR" />
                  <v itemID="20000003" minCount="1" maxCount="1" weight="100" locale="CN" />
                </group>
                <group dropGroupID="2" locale="CN" dropCount="1">
                  <v itemID="20000004" minCount="1" maxCount="1" weight="1" />
                </group>
              </dropBox>
              <dropBox dropBoxID="10001">
                <group dropGroupID="1" dropCount="1">
                  <v itemID="20000005" minCount="1" maxCount="1" weight="1" />
                </group>
              </dropBox>
            </ms2>
            """;
        string oldLocale = FeatureLocaleFilter.Locale;
        try {
            FeatureLocaleFilter.Locale = "KR";
            using XmlReader reader = XmlReader.Create(new StringReader(xml));
            var boxes = ServerDropTable.Read(reader).ToList();

            Assert.That(boxes.Select(box => box.Id), Is.EqualTo(new[] { 10000, 10001 }));
            var group = boxes[0].Box.group.Single();
            var rows = group.v.OrderBy(row => row.itemID).ToList();
            Assert.Multiple(() => {
                Assert.That(group.dropGroupID, Is.EqualTo(1));
                Assert.That(group.dropCount, Is.EqualTo(new[] { 1, 2 }));
                Assert.That(group.dropCountProbability, Is.EqualTo(new[] { 70, 30 }));
                Assert.That(group.smartDropRate, Is.EqualTo(100));
                Assert.That(rows.Select(row => row.itemID), Is.EqualTo(new[] { 20000001, 20000002 }));
                Assert.That((rows[0].minCount, rows[0].maxCount, rows[0].weight), Is.EqualTo((1, 3, 500)));
                Assert.That(rows[0].grade, Is.EqualTo(new short[] { 1, 2 }));
                Assert.That(rows[0].gradeProbability, Is.EqualTo(new[] { 60, 40 }));
                Assert.That((rows[1].minCount, rows[1].maxCount), Is.EqualTo((5, 5)));
                Assert.That(boxes[1].Box.group.Single().v.Single().itemID, Is.EqualTo(20000005));
            });
        } finally {
            FeatureLocaleFilter.Locale = oldLocale;
        }
    }
}
