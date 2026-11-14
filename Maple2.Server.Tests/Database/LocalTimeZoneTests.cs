using System;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Server.Core.Modules;
using Maple2.Server.Tests.Game;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Database;

// Times the database fills (column defaults, ON UPDATE) are read back as the server's local time, as the server's own
// writes are. Through the game database's options (GameDbModule.Options) each connection runs in the server's UTC
// offset, so a filled time is now in local time whatever zone the MySQL server keeps. On a server east or west of the
// database's zone the old times were off by the difference. The defaults are MySQL's: the tests need
// MS2_TEST_GAME_DB (they show a difference only when the database's zone is not the server's).
public class LocalTimeZoneTests {
    private static readonly string? Connection = Environment.GetEnvironmentVariable("MS2_TEST_GAME_DB");

    private DbContextOptions options = null!;

    [SetUp]
    public void SetUp() {
        if (Connection == null) {
            Assert.Ignore("the column defaults are MySQL's: set MS2_TEST_GAME_DB");
        }
        // On a UTC host the old and the new times are the same: only a host off UTC tells them apart.
        Assume.That(TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow), Is.Not.EqualTo(TimeSpan.Zero), "the host is on UTC");
        options = GameDbModule.Options(Connection);
    }

    private static void Now(DateTime time, DateTime before, DateTime after, string column) {
        Assert.That(time, Is.InRange(before.AddSeconds(-2), after.AddSeconds(2)), column);
    }

    [Test]
    public void Offset_IsTheLocalUtcOffset() {
        TimeSpan offset = TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow);
        string text = LocalTimeZoneInterceptor.Offset(DateTime.UtcNow);
        Assert.That(TimeSpan.Parse(text.TrimStart('+')), Is.EqualTo(offset));
        Assert.That(text, Does.Match(@"^[+-]\d\d:\d\d$"));
    }

    [Test]
    public void Session_RunsInTheServersOffset() {
        using var context = new Ms2Context(options);
        DateTime now = context.Database.SqlQueryRaw<DateTime>("SELECT NOW(6) AS `Value`").AsEnumerable().Single();
        Now(now, DateTime.Now, DateTime.Now, "NOW()");
    }

    // A new account, character and its first skill tab take CreationTime from the column default.
    [Test]
    public void NewRows_TakeLocalCreationTimes() {
        DateTime before = DateTime.Now;
        var test = new TestGameSession(options);
        DateTime after = DateTime.Now;

        using var context = new Ms2Context(options);
        Maple2.Database.Model.Account account = context.Account.AsNoTracking().Single(row => row.Id == test.AccountId);
        Maple2.Database.Model.Character character = context.Character.AsNoTracking().Single(row => row.Id == test.CharacterId);
        Maple2.Database.Model.SkillTab tab = context.SkillTab.AsNoTracking().First(row => row.CharacterId == test.CharacterId);
        Now(account.CreationTime, before, after, "account.CreationTime");
        Now(character.CreationTime, before, after, "character.CreationTime");
        Now(tab.CreationTime, before, after, "skill-tab.CreationTime");
    }

    // ON UPDATE: the character's LastModified is its last online time and the login's last save (entry mails).
    [Test]
    public void Update_TakesALocalLastModified() {
        var test = new TestGameSession(options);
        DateTime before = DateTime.Now;
        using (var context = new Ms2Context(options)) {
            context.Database.ExecuteSqlRaw("UPDATE `character` SET `Channel` = 3 WHERE `Id` = {0}", test.CharacterId);
        }
        DateTime after = DateTime.Now;

        using var read = new Ms2Context(options);
        Now(read.Character.AsNoTracking().Single(row => row.Id == test.CharacterId).LastModified, before, after, "character.LastModified");
    }
}
