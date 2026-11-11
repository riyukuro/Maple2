using System.IO;
using System.Xml.Serialization;
using Maple2.File.Ingest.Mapper;
using Maple2.File.Parser.Xml.Table;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;

namespace Maple2.Server.Tests.Ingest;

// The ingest of table/guildevent.xml over made-up rows: the parser's row type read from xml text, then
// the mapper's row of the server's guild table.
public class GuildEventIngestTests {
    private const string Xml = """
        <ms2>
            <guildEvent id="3" mapCode="777" />
            <guildEvent id="4" />
        </ms2>
        """;

    [Test]
    public void GuildEventRow_KeepsIdAndMap() {
        var root = (GuildEventRoot) new XmlSerializer(typeof(GuildEventRoot)).Deserialize(new StringReader(Xml))!;
        Assert.That(root.guildEvent, Has.Count.EqualTo(2));

        GuildTable.Event row = TableMapper.MapGuildEvent(root.guildEvent[0].id, root.guildEvent[0]);
        Assert.That(row, Is.EqualTo(new GuildTable.Event(Id: 3, MapId: 777)));
        // An open event with this row can be entered from another map; a row without a map cannot.
        Assert.That(GuildRules.CanEnterEvent(row.MapId, 1000, 1010, 100), Is.True);
        GuildTable.Event noMap = TableMapper.MapGuildEvent(root.guildEvent[1].id, root.guildEvent[1]);
        Assert.That(noMap, Is.EqualTo(new GuildTable.Event(Id: 4, MapId: 0)));
        Assert.That(GuildRules.CanEnterEvent(noMap.MapId, 1000, 1010, 100), Is.False);
    }
}
