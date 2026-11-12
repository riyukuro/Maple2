using System.Collections.Generic;
using System.Xml;
using Maple2.File.Ingest.Mapper;
using Maple2.File.Ingest.Utils;
using Maple2.Model.Metadata;

namespace Maple2.Server.Tests.Ingest;

// table/fieldwardata.xml: the rows of the features and locale the server has; a row without a pickWeight weighs 1;
// of rows with one fieldWarID the one the client's SelectFeatureElement picks.
public class FieldWarIngestTests {
    private const string Xml = """
        <ms2>
          <fieldWar fieldWarID="10000001" fieldWarGroupID="1" pickWeight="1" fieldID="02020061" rewardID="31000003" uiFieldID="02020003" feature="Fieldwar01" />
          <fieldWar fieldWarID="10000003" fieldWarGroupID="2" pickWeight="1" fieldID="02020063" rewardID="31000003" uiFieldID="02020009" feature="Fieldwar02" />
          <fieldWar fieldWarID="10000009" fieldWarGroupID="2" fieldID="02020069" feature="Fieldwar01" />
          <fieldWar fieldWarID="bad" fieldWarGroupID="2" fieldID="02020070" feature="Fieldwar01" />
        </ms2>
        """;

    [Test]
    public void Rows_OfEnabledFeatures() {
        var document = new XmlDocument();
        document.LoadXml(Xml);

        Dictionary<int, FieldWarMetadata> wars = ServerTableMapper.MapFieldWarData(document, row => row.GetAttribute("feature") == "Fieldwar01", Select);

        Assert.That(wars.Keys, Is.EquivalentTo(new[] { 10000001, 10000009 }));
        Assert.That(wars[10000001], Is.EqualTo(new FieldWarMetadata(10000001, 1, 1, 2020061, 31000003, 2020003)));
        Assert.That(wars[10000009], Is.EqualTo(new FieldWarMetadata(10000009, 2, 1, 2020069, 0, 0)));
    }

    private static readonly Dictionary<string, int> Levels = new() { ["Fieldwar01"] = 1, ["Fieldwar02"] = 2 };

    private static XmlElement Select(XmlElement a, XmlElement b) =>
        PlayedNpcStats.SelectFeatureElement(a, b, feature => Levels.GetValueOrDefault(feature), "KR");

    // One id twice: the higher feature level wins whatever the order; with one feature, the row of the locale, then
    // the row without one. A row of another locale only is not available.
    [Test]
    public void RepeatedId_TheClientsPick() {
        var document = new XmlDocument();
        document.LoadXml("""
            <ms2>
              <fieldWar fieldWarID="10000001" fieldWarGroupID="1" fieldID="02020061" feature="Fieldwar02" />
              <fieldWar fieldWarID="10000001" fieldWarGroupID="1" fieldID="02020062" feature="Fieldwar01" />
              <fieldWar fieldWarID="10000002" fieldWarGroupID="2" fieldID="02020063" feature="Fieldwar01" />
              <fieldWar fieldWarID="10000002" fieldWarGroupID="2" fieldID="02020064" feature="Fieldwar01" locale="KR" />
              <fieldWar fieldWarID="10000002" fieldWarGroupID="2" fieldID="02020065" feature="Fieldwar01" locale="NA" />
              <fieldWar fieldWarID="10000003" fieldWarGroupID="2" fieldID="02020066" feature="Fieldwar01" locale="NA" />
            </ms2>
            """);

        Dictionary<int, FieldWarMetadata> wars = ServerTableMapper.MapFieldWarData(document,
            row => row.GetAttribute("locale") is "" or "KR", Select);

        Assert.That(wars.Keys, Is.EquivalentTo(new[] { 10000001, 10000002 }));
        Assert.That(wars[10000001].MapId, Is.EqualTo(2020061));
        Assert.That(wars[10000002].MapId, Is.EqualTo(2020064));
    }
}
