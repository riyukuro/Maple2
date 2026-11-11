using System.IO;
using System.Xml.Serialization;
using Maple2.File.Ingest.Mapper;
using Maple2.File.Parser.Xml.Table;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;

namespace Maple2.Server.Tests.Ingest;

// The ingest of table/guildbuff.xml over a made-up row: the parser's row type read from xml text, then the
// mapper's row of the server's guild table.
public class GuildBuffIngestTests {
    private const string Xml = """
        <ms2>
            <guildBuff id="7" level="2" additionalEffectId="90000001" additionalEffectLevel="3" requireLevel="4"
                       upgradeCost="1500" cost="250" duration="45" cooltime="120" />
        </ms2>
        """;

    [Test]
    public void GuildBuffRow_KeepsDurationAndCooltime() {
        var root = (GuildBuffRoot) new XmlSerializer(typeof(GuildBuffRoot)).Deserialize(new StringReader(Xml))!;
        Assert.That(root.guildBuff, Has.Count.EqualTo(1));
        GuildBuff parsed = root.guildBuff[0];
        Assert.That(parsed.id, Is.EqualTo(7));
        Assert.That(parsed.level, Is.EqualTo(2));

        GuildTable.Buff row = TableMapper.MapGuildBuff(parsed);

        Assert.That(row, Is.EqualTo(new GuildTable.Buff(Id: 90000001, Level: 3, RequireLevel: 4, Cost: 250,
            UpgradeCost: 1500, Duration: 45, Cooldown: 120)));
        // Minutes: used at 1000, the effect ends at 1000 + 45 * 60 and the cooltime at 1000 + 120 * 60.
        Assert.That(GuildRules.SkillRemainingSeconds(row, 1000, 1000), Is.EqualTo(2700));
        Assert.That(GuildRules.SkillCooldownSeconds(row, 1000, 1000), Is.EqualTo(7200));
    }
}
