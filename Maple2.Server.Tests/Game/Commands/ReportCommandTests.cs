using System.CommandLine;
using System.CommandLine.IO;
using System.Linq;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Server.Game.Commands;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.Commands;

// The GM report command reads the reports players send: list (open, newest first), read one, close one.
public class ReportCommandTests {
    private TestGameSession test = null!;
    private GameSession session => test.Session;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.Start();
        using GameStorage.Request db = test.Storage.Context();
        db.ReportPlayer(11, "Target", 21, "Reporter", "spam", PlayerReportFlag.Behavior);
        db.ReportChat(12, "Talker", 22, "Other", "words", "bad line", ChatReportFlag.Spam);
    }

    private (int Code, string[] Lines) Run(params string[] args) {
        var console = new TestConsole();
        int code = new ReportCommand(session).Invoke(args, console);
        return (code, console.Out.ToString()!.Split('\n', System.StringSplitOptions.RemoveEmptyEntries));
    }

    [Test]
    public void List_ShowsTheOpenReportsNewestFirst() {
        (int code, string[] lines) = Run("list");
        Assert.Multiple(() => {
            Assert.That(code, Is.EqualTo(0));
            Assert.That(lines, Has.Length.EqualTo(2));
            Assert.That(lines[0], Does.StartWith("#2 ").And.Contain("Chat 'Talker' by 'Other': Spam: bad line"));
            Assert.That(lines[1], Does.StartWith("#1 ").And.Contain("Player 'Target' by 'Reporter': Behavior"));
        });
        Assert.That(Run("list", "-n", "1").Lines, Has.Length.EqualTo(1));
        Assert.That(Run("list", "-n", "0").Code, Is.EqualTo(1));
    }

    [Test]
    public void Read_ShowsTheReasonAndBothCharacters() {
        (int code, string[] lines) = Run("read", "1");
        Assert.Multiple(() => {
            Assert.That(code, Is.EqualTo(0));
            Assert.That(lines, Has.Some.EqualTo("Reported: 'Target' (character 11)"));
            Assert.That(lines, Has.Some.EqualTo("Reporter: 'Reporter' (character 21)"));
            Assert.That(lines, Has.Some.EqualTo("Reason: spam"));
        });
        Assert.That(Run("read", "9").Code, Is.EqualTo(1));
    }

    [Test]
    public void Close_TakesItOffTheOpenList() {
        Assert.That(Run("close", "1", "dealt", "with").Code, Is.EqualTo(0));

        string[] open = Run("list").Lines;
        Assert.That(open, Has.Length.EqualTo(1));
        Assert.That(open[0], Does.StartWith("#2 "));
        Assert.That(Run("list", "--all").Lines.Single(line => line.StartsWith("#1 ")), Does.EndWith("(closed)"));
        Assert.That(Run("read", "1").Lines, Has.Some.EqualTo($"Closed {ClosedTime()} by account {test.AccountId}"));

        (int again, string[] lines) = Run("close", "1");
        Assert.That(again, Is.EqualTo(1));
        Assert.That(lines, Is.EqualTo(new[] { "Report 1 is closed already." }));
        Assert.That(Run("close", "9").Lines, Is.EqualTo(new[] { "Report 9 not found." }));
    }

    private string ClosedTime() {
        using GameStorage.Request db = test.Storage.Context();
        return $"{db.GetPlayerReport(1)!.CloseTime:yyyy-MM-dd HH:mm}";
    }

    [Test]
    public void NeedsTheBanPermission() {
        var command = new ReportCommand(session);
        Assert.Multiple(() => {
            Assert.That(CommandRouter.Allowed(AdminPermissions.Ban, command), Is.True);
            Assert.That(CommandRouter.Allowed(AdminPermissions.GameMaster, command), Is.True);
            Assert.That(CommandRouter.Allowed(AdminPermissions.Debug, command), Is.False);
            Assert.That(CommandRouter.Allowed(AdminPermissions.None, command), Is.False);
        });
    }
}
