using System.Collections.Generic;
using System.Xml;
using Maple2.File.Ingest.Mapper;
using Maple2.Model.Metadata;

namespace Maple2.Server.Tests.Ingest;

// table/Server/hideAndSeek.xml and the map's xblock: the rides of each field and, per ride, the ObjectSearchIndex of
// the props of its model.
public class HideAndSeekIngestTests {
    private static XmlDocument Load(string xml) {
        var document = new XmlDocument();
        document.LoadXml(xml);
        return document;
    }

    [Test]
    public void XBlockSearchIndex_ByModel() {
        XmlDocument xblock = Load("""
            <game><entitySet>
              <entity modelName="Barrel_A_" name="a"><property name="ObjectSearchIndex"><set value="3"/></property></entity>
              <entity modelName="barrel_a" name="b"><property name="ObjectSearchIndex"><set value="7"/></property></entity>
              <entity modelName="Box_B" name="c"><property name="ObjectSearchIndex"><set value="x"/></property></entity>
              <entity modelName="Box_C" name="d"/>
            </entitySet></game>
            """);
        Dictionary<string, List<int>> props = ServerTableMapper.MapXBlockSearchIndex(xblock);
        Assert.That(props.Keys, Is.EquivalentTo(new[] { "barrel_a" }));
        Assert.That(props["barrel_a"], Is.EqualTo(new[] { 3, 7 }));
    }

    // A row of a locale replaces the row without one in either order; filtered rows are left out; a ride without a
    // prop of its model stays a ride without props.
    [Test]
    public void Fields_LocaleRowReplaces() {
        XmlDocument table = Load("""
            <ms2>
              <field mapCode="61000023" ridingObject="1,2" locale="KR"/>
              <field mapCode="61000023" ridingObject="9"/>
              <field mapCode="61000024" ridingObject="2, 3,x"/>
              <field mapCode="61000025" ridingObject="1" feature="Off"/>
            </ms2>
            """);
        var nifs = new Dictionary<int, string> { [1] = "Barrel_A", [2] = "box_b", [3] = "none" };
        HideAndSeekTable result = ServerTableMapper.MapHideAndSeek(table, row => row.GetAttribute("feature") != "Off",
            id => nifs.GetValueOrDefault(id), mapId => new Dictionary<string, List<int>> {
                ["barrel_a"] = [mapId == 61000023 ? 3 : 4], ["box_b"] = [5, 6],
            });
        Assert.That(result.Entries.Keys, Is.EquivalentTo(new[] { 61000023, 61000024 }));
        Assert.Multiple(() => {
            Assert.That(result.Entries[61000023].Rides, Is.EqualTo(new[] { 1, 2 }));
            Assert.That(result.Entries[61000023].Props[1], Is.EqualTo(new[] { 3 }));
            Assert.That(result.Entries[61000023].Props[2], Is.EqualTo(new[] { 5, 6 }));
            Assert.That(result.Entries[61000024].Rides, Is.EqualTo(new[] { 2, 3 }));
            Assert.That(result.Entries[61000024].Props.Keys, Is.EquivalentTo(new[] { 2 }));
        });
    }
}
