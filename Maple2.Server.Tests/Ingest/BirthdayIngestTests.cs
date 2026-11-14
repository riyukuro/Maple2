using System.Collections.Generic;
using System.Numerics;
using System.Xml;
using Maple2.File.Ingest.Mapper;
using Maple2.Model.Metadata;

namespace Maple2.Server.Tests.Ingest;

// table/birthdaytable.xml and the fields' xblocks: the npcs of each field at the event spawn point of their
// spawnPointID (the field's own xblock first, then its locale variants), the banners, the day range.
public class BirthdayIngestTests {
    private static XmlDocument Load(string xml) {
        var document = new XmlDocument();
        document.LoadXml(xml);
        return document;
    }

    private const string Table = """
        <ms2><birthday dayRange="1-7">
          <spawn fieldID="2000001"><npc><v npcID="11003587" spawnPointID="101"/><v npcID="11003588" spawnPointID="102"/></npc><ugcBanner><v bannerID="10221"/></ugcBanner></spawn>
          <spawn fieldID="2000064"><npc><v npcID="11003587" spawnPointID="101"/><v npcID="11003588" spawnPointID="102"/></npc><ugcBanner><v bannerID="10222"/></ugcBanner></spawn>
        </birthday></ms2>
        """;

    [Test]
    public void EventSpawnPoints_PositionRotationId() {
        XmlDocument xblock = Load("""
            <game><entitySet>
              <entity modelName="EventSpawnPointNPC" name="11003587_eventcake"><property name="Position"><set value="-2400, 1500, 3450"/></property><property name="Rotation"><set value="0, 0, 315"/></property></entity>
              <entity modelName="EventSpawnPointNPC" name="11003588_ducky"><property name="Position"><set value="-2550, 1500, 3450"/></property><property name="SpawnPointID"><set value="102"/></property></entity>
              <entity modelName="SpawnPointNPC" name="other"><property name="SpawnPointID"><set value="101"/></property></entity>
            </entitySet></game>
            """);
        List<ServerTableMapper.BirthdaySpawnPoint> points = ServerTableMapper.MapEventSpawnPoints(xblock);
        Assert.That(points, Is.EqualTo(new[] {
            new ServerTableMapper.BirthdaySpawnPoint("11003587_eventcake", null, new Vector3(-2400, 1500, 3450), new Vector3(0, 0, 315)),
            new ServerTableMapper.BirthdaySpawnPoint("11003588_ducky", 102, new Vector3(-2550, 1500, 3450), Vector3.Zero),
        }));
    }

    // The own xblock's point wins over a variant's; a point the own xblock lacks comes from the variant; the cake point
    // without a SpawnPointID is found by its name; a field no xblock places keeps its banners and no npcs.
    [Test]
    public void Fields_OwnXBlockThenVariant() {
        var own = new List<ServerTableMapper.BirthdaySpawnPoint> {
            new("x", 102, new Vector3(1, 1, 1), new Vector3(0, 0, 1)),
        };
        var variant = new List<ServerTableMapper.BirthdaySpawnPoint> {
            new("11003587_eventcake", null, new Vector3(2, 2, 2), new Vector3(0, 0, 2)),
            new("y", 102, new Vector3(3, 3, 3), new Vector3(0, 0, 3)),
        };
        BirthdayTable table = ServerTableMapper.MapBirthday(Load(Table),
            mapId => mapId == 2000001 ? [own, variant] : []);

        Assert.That((table.FirstDay, table.LastDay), Is.EqualTo((1, 7)));
        Assert.That(table.Fields[2000001].Npcs, Is.EqualTo(new[] {
            new BirthdayTable.Npc(11003587, 101, new Vector3(2, 2, 2), new Vector3(0, 0, 2)),
            new BirthdayTable.Npc(11003588, 102, new Vector3(1, 1, 1), new Vector3(0, 0, 1)),
        }));
        Assert.That(table.Fields[2000001].Banners, Is.EqualTo(new[] { 10221 }));
        Assert.That(table.Fields[2000064].Npcs, Is.Empty);
        Assert.That(table.Fields[2000064].Banners, Is.EqualTo(new[] { 10222 }));
    }

    // A single day is that day only; an unreadable range is every day.
    [TestCase("3", 3, 3)]
    [TestCase("2-5", 2, 5)]
    [TestCase("x", 1, 7)]
    [TestCase("", 1, 7)]
    public void DayRange(string range, int first, int last) {
        BirthdayTable table = ServerTableMapper.MapBirthday(Load($"""<ms2><birthday dayRange="{range}"/></ms2>"""), _ => []);
        Assert.That((table.FirstDay, table.LastDay), Is.EqualTo((first, last)));
        Assert.That(table.Fields, Is.Empty);
    }
}
