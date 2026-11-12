using System.Collections.Generic;
using System.Xml;
using Maple2.File.Ingest.Mapper;
using Maple2.Model.Metadata;

namespace Maple2.Server.Tests.Ingest;

// table/fieldwardata.xml: the rows of the features the locale has; a row without a pickWeight weighs 1.
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

        Dictionary<int, FieldWarMetadata> wars = ServerTableMapper.MapFieldWarData(document, row => row.GetAttribute("feature") == "Fieldwar01");

        Assert.That(wars.Keys, Is.EquivalentTo(new[] { 10000001, 10000009 }));
        Assert.That(wars[10000001], Is.EqualTo(new FieldWarMetadata(10000001, 1, 1, 2020061, 31000003, 2020003)));
        Assert.That(wars[10000009], Is.EqualTo(new FieldWarMetadata(10000009, 2, 1, 2020069, 0, 0)));
    }
}
