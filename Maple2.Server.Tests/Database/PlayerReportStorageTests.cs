using System.Linq;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Server.Tests.Game;
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
