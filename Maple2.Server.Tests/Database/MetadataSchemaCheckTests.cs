using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Context;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Database;

// The start-up check that the metadata database has every column the model reads. The statements are checked on
// the model alone; the run against a database is skipped unless MS2_TEST_DATA_DB holds the connection string of a
// scratch MySQL metadata database written by the current Maple2.File.Ingest.
public class MetadataSchemaCheckTests {
    private static readonly string? Connection = Environment.GetEnvironmentVariable("MS2_TEST_DATA_DB");

    private static MetadataContext InMemory() =>
        new(new DbContextOptionsBuilder().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Test]
    public void Probes_NameEveryColumnOfTheTrophyTable() {
        using MetadataContext context = InMemory();
        Dictionary<string, string> probes = MetadataSchemaCheck.Probes(context.Model).ToDictionary(probe => probe.Table, probe => probe.Sql);

        Assert.That(probes, Contains.Key("achievement"));
        string sql = probes["achievement"];
        Assert.That(sql, Does.StartWith("SELECT `"));
        Assert.That(sql, Does.EndWith(" FROM `achievement` LIMIT 0"));
        foreach (string column in new[] { "Id", "Name", "AccountWide", "Category", "CategoryTags", "Grades", "Locked" }) {
            Assert.That(sql, Does.Contain($"`{column}`"));
        }
    }

    [Test]
    public void Probes_CoverEveryTableOnce() {
        using MetadataContext context = InMemory();
        List<string> tables = MetadataSchemaCheck.Probes(context.Model).Select(probe => probe.Table).ToList();

        Assert.That(tables, Is.Unique);
        Assert.That(tables, Is.SupersetOf(new[] { "achievement", "additional-effect", "ugcmap" }));
        Assert.That(tables, Has.Count.EqualTo(context.Model.GetEntityTypes().Count()));
    }

    [Test]
    public void StaleTables_NoneInACurrentDatabase() {
        if (Connection == null) {
            Assert.Ignore("MS2_TEST_DATA_DB is not set");
        }
        using var context = new MetadataContext(new DbContextOptionsBuilder().UseMySql(Connection, ServerVersion.AutoDetect(Connection)).Options);

        Assert.That(MetadataSchemaCheck.StaleTables(context), Is.Empty);
    }

    // The table an older ingest wrote: the same table without one column.
    [Test]
    public void StaleTables_NamesATableThatLacksAColumn() {
        if (Connection == null) {
            Assert.Ignore("MS2_TEST_DATA_DB is not set");
        }
        using var context = new MetadataContext(new DbContextOptionsBuilder().UseMySql(Connection, ServerVersion.AutoDetect(Connection)).Options);

        context.Database.ExecuteSqlRaw("ALTER TABLE `achievement` RENAME COLUMN `Locked` TO `LockedAway`");
        try {
            Assert.That(MetadataSchemaCheck.StaleTables(context), Is.EqualTo(new[] { "achievement" }));
        } finally {
            context.Database.ExecuteSqlRaw("ALTER TABLE `achievement` RENAME COLUMN `LockedAway` TO `Locked`");
        }
        Assert.That(MetadataSchemaCheck.StaleTables(context), Is.Empty);
    }
}
