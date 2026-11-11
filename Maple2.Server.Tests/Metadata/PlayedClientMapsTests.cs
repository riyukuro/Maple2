using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Xml;
using Maple2.Model.Metadata;

namespace Maple2.Server.Tests.Metadata;

// MS2_PLAYED_CLIENT_XML: which maps the played client's field table marks as not loadable.
public class PlayedClientMapsTests {
    private static XmlDocument Table(string rows) {
        var document = new XmlDocument();
        document.LoadXml($"<ms2>{rows}</ms2>");
        return document;
    }

    [Test]
    public void Missing_IsTheRowsWithoutAnXBlockName() {
        XmlDocument table = Table("""
            <fieldData id="900001"><environment><xblock name="900001_AA" /><bgm id="1" /></environment></fieldData>
            <fieldData id="900002" />
            <fieldData id="900003"><environment><bgm id="1" /></environment></fieldData>
            <fieldData id="900004"><environment><xblock name="" /></environment></fieldData>
            <fieldData id="900005"><environment feature="A"><bgm id="1" /></environment><environment feature="B"><xblock name="900005_AA" /></environment></fieldData>
            <fieldData id="x" />
            """);

        Assert.That(PlayedClientMaps.Missing(table).OrderBy(id => id), Is.EqualTo(new[] { 900002, 900003, 900004 }));
    }

    [Test]
    public void Missing_TableWithoutRows_IsEmpty() {
        Assert.That(PlayedClientMaps.Missing(Table("")), Is.Empty);
        Assert.That(PlayedClientMaps.Missing(Table("<v key=\"a\" />")), Is.Empty);
    }

    // A map with an empty row or with no row at all is lacking; a table without rows marks nothing.
    [Test]
    public void Lacks_EmptyRowOrNoRow() {
        XmlDocument table = Table("""
            <fieldData id="900001"><environment><xblock name="900001_AA" /></environment></fieldData>
            <fieldData id="900002" />
            """);
        HashSet<int> rows = PlayedClientMaps.Rows(table);
        HashSet<int> missing = PlayedClientMaps.Missing(table);

        Assert.That(rows.OrderBy(id => id), Is.EqualTo(new[] { 900001, 900002 }));
        Assert.That(PlayedClientMaps.Lacks(rows, missing, 900001), Is.False);
        Assert.That(PlayedClientMaps.Lacks(rows, missing, 900002), Is.True, "empty row");
        Assert.That(PlayedClientMaps.Lacks(rows, missing, 900003), Is.True, "no row");
        Assert.That(PlayedClientMaps.Lacks([], [], 900003), Is.False, "no table");
    }

    // The mark is kept in the property JSON: absent in rows ingested before (reads false), kept when set.
    [Test]
    public void Mark_IsFalseWhenAbsentFromTheJson() {
        var property = new MapMetadataProperty(default, default, 0, default, false, 0, 0, 0, 0, default, 0, false, false, false,
            false, false, false, false, false, false, false, 0);
        Assert.That(property.NotInPlayedClient, Is.False);

        string json = JsonSerializer.Serialize(property);
        var node = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
        node.Remove("NotInPlayedClient");
        Assert.That(JsonSerializer.Deserialize<MapMetadataProperty>(node.ToJsonString())!.NotInPlayedClient, Is.False);

        string marked = JsonSerializer.Serialize(property with { NotInPlayedClient = true });
        Assert.That(JsonSerializer.Deserialize<MapMetadataProperty>(marked)!.NotInPlayedClient, Is.True);
    }
}
