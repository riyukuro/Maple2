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

    // The client's field skillUseDisable list: from the available environment's <property>, empty where it lists none,
    // and no entry for a row with no available environment.
    [Test]
    public void SkillUseDisable_IsTheAvailableEnvironmentsList() {
        XmlDocument table = Table("""
            <fieldData id="51000001"><environment><property skillUseDisable="1,2,3,5" /></environment></fieldData>
            <fieldData id="900002"><environment><property /></environment></fieldData>
            <fieldData id="900003"><environment feature="Off"><property skillUseDisable="1" /></environment></fieldData>
            <fieldData id="900004"><environment><property skillUseDisable="4" /></environment><environment feature="Off"><property skillUseDisable="1" /></environment></fieldData>
            <fieldData id="900005" />
            """);
        Dictionary<int, int[]> lists = PlayedClientMaps.SkillUseDisable(table, environment => environment.GetAttribute("feature") != "Off");

        Assert.That(lists[51000001], Is.EqualTo(new[] { 1, 2, 3, 5 }));
        Assert.That(lists[900002], Is.Empty);
        Assert.That(lists.ContainsKey(900003), Is.False, "no available environment");
        Assert.That(lists[900004], Is.EqualTo(new[] { 4 }), "the one that is off is skipped");
        Assert.That(lists.ContainsKey(900005), Is.False, "no environment");
    }

    // The client's useEPSkill: KR 61000003 (MassiveOxQuiz) sets 0 in its <ui>; 02000001 sets 1; a <ui> without the
    // attribute is 1 (fielddata.h:118); a row with no available environment has no entry. The data pack's map xml
    // has 0 for 00000001, which the client allows.
    [Test]
    public void UseEpSkill_IsTheAvailableEnvironmentsUiFlag() {
        XmlDocument table = Table("""
            <fieldData id="61000003"><environment><ui useEPSkill="0" /></environment></fieldData>
            <fieldData id="2000001"><environment><ui useEPSkill="1" /></environment></fieldData>
            <fieldData id="1"><environment><ui /></environment></fieldData>
            <fieldData id="900003"><environment feature="Off"><ui useEPSkill="0" /></environment></fieldData>
            <fieldData id="900004"><environment><ui useEPSkill="0" /></environment><environment feature="Off"><ui useEPSkill="1" /></environment></fieldData>
            """);
        Dictionary<int, bool> flags = PlayedClientMaps.UseEpSkill(table, environment => environment.GetAttribute("feature") != "Off");

        Assert.That(flags[61000003], Is.False);
        Assert.That(flags[2000001], Is.True);
        Assert.That(flags[1], Is.True, "no attribute");
        Assert.That(flags.ContainsKey(900003), Is.False, "no available environment");
        Assert.That(flags[900004], Is.False, "the one that is off is skipped");
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
