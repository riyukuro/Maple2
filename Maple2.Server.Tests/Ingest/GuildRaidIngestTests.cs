using System.Collections.Generic;
using System.Xml;
using M2dXmlGenerator;
using Maple2.File.Ingest.Mapper;
using Maple2.Model.Metadata;

namespace Maple2.Server.Tests.Ingest;

// guildraid.xml rows of the client's shape (made-up rooms and values), read as CGuildXmlData::GuildRaid reads them.
[NonParallelizable]
public class GuildRaidIngestTests {
    private Dictionary<string, int> oldFeatures = null!;
    private string oldLocale = null!;

    [SetUp]
    public void SetFilter() {
        oldFeatures = FeatureLocaleFilter.Features;
        oldLocale = FeatureLocaleFilter.Locale;
        FeatureLocaleFilter.Locale = "KR";
        FeatureLocaleFilter.Features = new Dictionary<string, int> { ["TestRaid01"] = 1 };
    }

    [TearDown]
    public void RestoreFilter() {
        FeatureLocaleFilter.Features = oldFeatures;
        FeatureLocaleFilter.Locale = oldLocale;
    }

    [Test]
    public void ReadsTheRowsOfEnabledFeatures() {
        var document = new XmlDocument();
        document.LoadXml("""
            <ms2>
              <guildRaid dungeonRoomID="70001" clearRaidPoint="300" clearGuildFund="1.5" clearGuildExp="1.25" feature="TestRaid01" />
              <guildRaid dungeonRoomID="70002" clearRaidPoint="40" feature="TestRaid01" />
              <guildRaid dungeonRoomID="70003" clearRaidPoint="9" feature="TestRaidOff" />
              <guildRaid dungeonRoomID="0" clearRaidPoint="9" feature="TestRaid01" />
            </ms2>
            """);

        Dictionary<int, GuildTable.Raid> rows = TableMapper.GuildRaids(document);

        Assert.That(rows.Keys, Is.EquivalentTo(new[] { 70001, 70002 }));
        Assert.That(rows[70001], Is.EqualTo(new GuildTable.Raid(DungeonRoomId: 70001, ClearRaidPoint: 300, ClearGuildFund: 1.5f, ClearGuildExp: 1.25f)));
        // The client's factors start at 1 when the row leaves them out.
        Assert.That(rows[70002], Is.EqualTo(new GuildTable.Raid(DungeonRoomId: 70002, ClearRaidPoint: 40, ClearGuildFund: 1f, ClearGuildExp: 1f)));
    }
}
