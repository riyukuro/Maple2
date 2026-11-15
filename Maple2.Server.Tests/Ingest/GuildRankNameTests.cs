using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Xml;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.File.Ingest.Mapper;
using Maple2.Model.Common;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Ingest;

// The client's default guild rank names from stringcommon.xml (s_guild_grade_default_*), and how a stored rank name is
// shown: a guild made on KR data stores the KR defaults, which a client in another language shows as its own defaults.
public class GuildRankNameTests {
    private static readonly string[] English = ["Leader", "Co-Leader", "Guild Member", "Initiate", "Guild Member 2", "Initiate 2"];

    private static XmlDocument Strings(params (string Id, string Value)[] keys) {
        var document = new XmlDocument();
        document.LoadXml("<ms2>" + string.Concat(keys.Select(key => $"<key id=\"{key.Id}\" value=\"{key.Value}\" feature=\"Any01\" />")) + "</ms2>");
        return document;
    }

    [Test]
    public void Parse_TakesTheSixKeys_MasterFirst() {
        GuildRankNameTable table = TableMapper.GuildRankNames(Strings(
            ("s_guild_grade_default_group5", English[5]), ("s_other", "x"), ("s_guild_grade_default_master", English[0]),
            ("s_guild_grade_default_group1", English[1]), ("s_guild_grade_default_group2", English[2]),
            ("s_guild_grade_default_group3", English[3]), ("s_guild_grade_default_group4", English[4])));
        Assert.That(table.Names, Is.EqualTo(English));
    }

    [Test]
    public void Parse_AMissingKeyIsEmpty_NoKeyIsTheEmptyTable() {
        GuildRankNameTable table = TableMapper.GuildRankNames(Strings(("s_guild_grade_default_group2", "\u00C9quipe")));
        Assert.That(table.Names, Is.EqualTo(new[] { "", "", "\u00C9quipe", "", "", "" }));
        Assert.That(TableMapper.GuildRankNames(Strings(("s_other", "x"))), Is.SameAs(GuildRankNameTable.Empty));
    }

    // A KR guild's stored defaults show as the client's; a typed name, a default at another rank's place and a rank
    // past the six are kept.
    // The region's file comes first: its names win over the English ones, and English fills a key it lacks or leaves
    // empty. Each key says which file it came from.
    [Test]
    public void Parse_TheRegionFileWins_EnglishFillsAKeyItLacks() {
        XmlDocument kr = Strings(
            ("s_guild_grade_default_master", "Leader"), ("s_guild_grade_default_group1", "Officer"),
            ("s_guild_grade_default_group2", "Member"), ("s_guild_grade_default_group3", ""),
            ("s_guild_grade_default_group4", "\uAE38\uB4DC\uC6D02"));
        XmlDocument en = Strings(English.Select((name, i) => (GuildRankNameTable.Keys[i], name)).ToArray());

        GuildRankNameTable table = TableMapper.GuildRankNames([kr, en], out int[] from);
        Assert.That(table.Names, Is.EqualTo(new[] { "Leader", "Officer", "Member", English[3], "\uAE38\uB4DC\uC6D02", English[5] }));
        Assert.That(from, Is.EqualTo(new[] { 0, 0, 0, 1, 0, 1 }));

        GuildRankNameTable none = TableMapper.GuildRankNames([Strings(("s_other", "x"))], out int[] nowhere);
        Assert.That(none, Is.SameAs(GuildRankNameTable.Empty));
        Assert.That(nowhere, Is.EqualTo(new[] { -1, -1, -1, -1, -1, -1 }));
        Assert.That(TableMapper.GuildRankNames([], out _), Is.SameAs(GuildRankNameTable.Empty));
    }

    // The client's string folder per region: its own language, English on NA.
    [TestCase("KR", "kr")]
    [TestCase("NA", "en")]
    [TestCase("CN", "cn")]
    [TestCase("JP", "jp")]
    public void StringDirectory_IsTheRegionsLanguage(string locale, string directory) {
        Assert.That(TableMapper.StringDirectory(locale), Is.EqualTo(directory));
    }

    [Test]
    public void Shown_MapsOnlyTheDataDefaultAtItsPlace() {
        IReadOnlyList<string> kr = GuildRules.DefaultRankNames("KR");
        var table = new GuildRankNameTable(English);
        for (int i = 0; i < 6; i++) {
            Assert.That(table.Shown(i, kr[i], kr), Is.EqualTo(English[i]));
        }
        Assert.That(table.Shown(0, "Abc", kr), Is.EqualTo("Abc"));
        Assert.That(table.Shown(0, kr[1], kr), Is.EqualTo(kr[1]));
        Assert.That(table.Shown(6, kr[0], kr), Is.EqualTo(kr[0]));
        Assert.That(table.Shown(-1, kr[0], kr), Is.EqualTo(kr[0]));
        // The old English defaults of guilds made before are names like any other.
        Assert.That(table.Shown(0, "Master", kr), Is.EqualTo("Master"));
    }

    [Test]
    public void Shown_WithoutTheClientName_KeepsTheStoredOne() {
        IReadOnlyList<string> kr = GuildRules.DefaultRankNames("KR");
        Assert.That(GuildRankNameTable.Empty.Shown(0, kr[0], kr), Is.EqualTo(kr[0]));
        var partial = new GuildRankNameTable(["", "Co-Leader", "", "", "", ""]);
        Assert.That(partial.Shown(0, kr[0], kr), Is.EqualTo(kr[0]));
        Assert.That(partial.Shown(1, kr[1], kr), Is.EqualTo("Co-Leader"));
    }

    // On NA data the defaults already are the English names: an English client sees them unchanged.
    [Test]
    public void Shown_OnNaData_IsTheSameName() {
        IReadOnlyList<string> na = GuildRules.DefaultRankNames("NA");
        var table = new GuildRankNameTable(English);
        for (int i = 0; i < 6; i++) {
            Assert.That(table.Shown(i, na[i], na), Is.EqualTo(na[i]));
        }
    }

    // The reverse of Shown: the client default at its place goes back as the data default; anything else as sent.
    [Test]
    public void Stored_MapsOnlyTheClientDefaultAtItsPlace() {
        IReadOnlyList<string> kr = GuildRules.DefaultRankNames("KR");
        var table = new GuildRankNameTable(English);
        for (int i = 0; i < 6; i++) {
            Assert.That(table.Stored(i, English[i], kr), Is.EqualTo(kr[i]));
            Assert.That(table.Shown(i, table.Stored(i, English[i], kr), kr), Is.EqualTo(English[i]));
        }
        Assert.That(table.Stored(0, English[1], kr), Is.EqualTo(English[1]));
        Assert.That(table.Stored(0, "Abc", kr), Is.EqualTo("Abc"));
        Assert.That(table.Stored(6, English[0], kr), Is.EqualTo(English[0]));
        Assert.That(GuildRankNameTable.Empty.Stored(0, English[0], kr), Is.EqualTo(English[0]));
        Assert.That(new GuildRankNameTable(["", "", "", "", "", ""]).Stored(0, "", kr), Is.EqualTo(""));
    }

    [Test]
    public void Table_RoundTripsAsJson() {
        Table table = new GuildRankNameTable(English);
        var back = (GuildRankNameTable) JsonSerializer.Deserialize<Table>(JsonSerializer.Serialize(table))!;
        Assert.That(back.Names, Is.EqualTo(English));
    }

    // An old metadata db has no rank name row: the storage gives the empty table, so names are shown as stored.
    [Test]
    public void AMissingRow_GivesTheFallback() {
        using var context = new MetadataContext(new DbContextOptionsBuilder().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        MethodInfo retrieve = typeof(TableMetadataStorage).GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(method => method.Name == "Retrieve" && method.GetParameters().Length == 3)
            .MakeGenericMethod(typeof(GuildRankNameTable));
        var missing = (Lazy<GuildRankNameTable>) retrieve.Invoke(null, [context, TableNames.GUILD_RANK_NAMES, GuildRankNameTable.Empty])!;
        Assert.That(missing.Value, Is.SameAs(GuildRankNameTable.Empty));

        context.TableMetadata.Add(new TableMetadata { Name = TableNames.GUILD_RANK_NAMES, Table = new GuildRankNameTable(English) });
        context.SaveChanges();
        var present = (Lazy<GuildRankNameTable>) retrieve.Invoke(null, [context, TableNames.GUILD_RANK_NAMES, GuildRankNameTable.Empty])!;
        Assert.That(present.Value.Names, Is.EqualTo(English));
    }
}
