using System;
using System.Data.Common;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Server.Tests.Game;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NUnit.Framework;

namespace Maple2.Server.Tests.Database;

// The reports players send are read and closed by the GM report command: newest first, the open ones unless asked.
public class PlayerReportStorageTests {
    private TestGameSession test = null!;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        using GameStorage.Request db = test.Storage.Context();
        db.ReportPlayer(11, "Target", 21, "Reporter", "spam", PlayerReportFlag.Behavior);
        db.ReportChat(12, "Talker", 22, "Other", "words", "bad line", ChatReportFlag.Spam);
    }

    [Test]
    public void List_NewestFirstWithTheDetail() {
        using GameStorage.Request db = test.Storage.Context();
        PlayerReportEntry[] reports = db.GetPlayerReports(false, 10).ToArray();

        Assert.That(reports.Select(report => report.PlayerName), Is.EqualTo(new[] { "Talker", "Target" }));
        Assert.That(reports[0].Category, Is.EqualTo(ReportCategory.Chat));
        Assert.That(reports[0].Detail, Is.EqualTo("Spam: bad line"));
        Assert.That(reports[1].Detail, Is.EqualTo("Behavior"));
        Assert.That(reports[1].ReporterCharacterId, Is.EqualTo(21));
        Assert.That(reports.All(report => report.CloseTime == null), Is.True);
        Assert.That(db.GetPlayerReports(false, 1).Single().PlayerName, Is.EqualTo("Talker"));
    }

    [Test]
    public void Close_OnceThenListedOnlyWithClosed() {
        using GameStorage.Request db = test.Storage.Context();
        long id = db.GetPlayerReports(false, 10).Single(report => report.PlayerName == "Target").Id;

        PlayerReportEntry? closed = db.ClosePlayerReport(id, 500);
        Assert.That(closed?.CloseTime, Is.Not.Null);
        Assert.That(closed!.ClosedBy, Is.EqualTo(500));
        Assert.That(db.ClosePlayerReport(id, 501), Is.Null);
        Assert.That(db.ClosePlayerReport(999, 500), Is.Null);

        Assert.That(db.GetPlayerReports(false, 10).Select(report => report.PlayerName), Is.EqualTo(new[] { "Talker" }));
        Assert.That(db.GetPlayerReports(true, 10).Count, Is.EqualTo(2));
        Assert.That(db.GetPlayerReport(id)?.ClosedBy, Is.EqualTo(500));
        Assert.That(db.GetPlayerReport(999), Is.Null);
    }
}

// On MySQL the close is one update that tests CloseTime. Another GM (another channel) closes the report between
// this close's read and its write: the write changes no row, so this GM is told it is closed and ClosedBy stays the
// other GM's (the old read-check-save overwrote it and told both "Closed"). Needs MS2_TEST_GAME_DB, a scratch MySQL
// game database.
public class PlayerReportCloseMySqlTests {
    private static readonly string? Connection = Environment.GetEnvironmentVariable("MS2_TEST_GAME_DB");

    [Test]
    public void Close_ByAnotherGmMeanwhile_IsNotOverwritten() {
        if (Connection == null) {
            Assert.Ignore("the conditional close needs MySQL: set MS2_TEST_GAME_DB");
        }
        ServerVersion version = ServerVersion.AutoDetect(Connection);
        DbContextOptions plain = new DbContextOptionsBuilder().UseMySql(Connection, version).Options;
        var hook = new CommandHook();
        var test = new TestGameSession(new DbContextOptionsBuilder().UseMySql(Connection, version).AddInterceptors(hook).Options);
        long id;
        using (GameStorage.Request db = test.Storage.Context()) {
            db.ReportPlayer(11, "Target", 21, "Reporter", "spam", PlayerReportFlag.Behavior);
            id = db.GetPlayerReports(false, 1).Single().Id;
        }

        DateTime? otherTime = DateTime.Now;
        bool other = false;
        hook.Before = command => {
            if (other || !command.CommandText.Contains("UPDATE `player-reports`")) {
                return;
            }
            other = true;
            using var context = new Ms2Context(plain);
            context.PlayerReports.Where(report => report.Id == id)
                .ExecuteUpdate(update => update.SetProperty(report => report.CloseTime, otherTime).SetProperty(report => report.ClosedBy, 500L));
        };

        using GameStorage.Request gm = test.Storage.Context();
        PlayerReportEntry? closed = gm.ClosePlayerReport(id, 501);
        hook.Before = null;
        Assert.Multiple(() => {
            Assert.That(other, Is.True);
            Assert.That(closed, Is.Null);
            Assert.That(gm.GetPlayerReport(id)!.ClosedBy, Is.EqualTo(500));
            Assert.That(gm.ClosePlayerReport(id, 501), Is.Null);
        });

        using (GameStorage.Request db = test.Storage.Context()) {
            db.ReportPlayer(11, "Target", 21, "Reporter", "again", PlayerReportFlag.Behavior);
            long open = db.GetPlayerReports(false, 1).Single().Id;
            Assert.That(db.ClosePlayerReport(open, 501)?.ClosedBy, Is.EqualTo(501));
        }
    }

    private sealed class CommandHook : DbCommandInterceptor {
        public Action<DbCommand>? Before;

        public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result) {
            Before?.Invoke(command);
            return result;
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData,
                                                                          InterceptionResult<DbDataReader> result) {
            Before?.Invoke(command);
            return result;
        }
    }
}
