using System.CommandLine;
using System.CommandLine.IO;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Server.Game.Commands;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.Commands;

// The GM report command reads the reports players send: list (open, newest first), read one, close one, and mutes
// (restrict kind 6) or bans the reported character's account through restrict and ban, closing the report.
public class ReportCommandTests {
    private TestGameSession test = null!;
    private GameSession session => test.Session;

    private const long OffenderAccount = 2000;
    private long offender;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.Start();
        using GameStorage.Request db = test.Storage.Context();
        db.ReportPlayer(11, "Target", 21, "Reporter", "spam", PlayerReportFlag.Behavior);
        db.ReportChat(12, "Talker", 22, "Other", "words", "bad line", ChatReportFlag.Spam);

        // Report 3 is on a character that exists, since renamed: mute and ban act on the account by the current name.
        using (var context = new Ms2Context(test.Options)) {
            Maple2.Database.Model.Account account = new Account { Username = "offender" };
            account.Id = OffenderAccount;
            context.Account.Add(account);
            context.SaveChanges();
        }
        offender = db.CreateCharacter(new Character {
            AccountId = OffenderAccount,
            Name = "Renamed",
            Job = Job.Newbie,
            Mastery = new Mastery(),
            MapId = 2000023,
            ReturnMaps = new Maple2.Tools.LimitedStack<int>(3, 2000023),
        })!.Id;
        db.ReportPlayer(offender, "OldName", 21, "Reporter", "spam", PlayerReportFlag.Behavior);
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
            Assert.That(lines, Has.Length.EqualTo(3));
            Assert.That(lines[0], Does.StartWith("#3 "));
            Assert.That(lines[1], Does.StartWith("#2 ").And.Contain("Chat 'Talker' by 'Other': Spam: bad line"));
            Assert.That(lines[2], Does.StartWith("#1 ").And.Contain("Player 'Target' by 'Reporter': Behavior"));
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
        Assert.That(open.Select(line => line.Split(' ')[0]), Is.EqualTo(new[] { "#3", "#2" }));
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

    private AccountBlock[] Blocks() {
        using GameStorage.Request db = test.Storage.Context();
        return db.GetAccountBlocks(OffenderAccount).ToArray();
    }

    private bool Closed(long id) {
        using GameStorage.Request db = test.Storage.Context();
        return db.GetPlayerReport(id)!.CloseTime != null;
    }

    [Test]
    public void Mute_RestrictsChatByTheCurrentNameAndCloses() {
        (int code, string[] lines) = Run("mute", "3", "12h", "chat", "spam");

        Assert.Multiple(() => {
            Assert.That(code, Is.EqualTo(0));
            Assert.That(lines.Last(), Is.EqualTo("Closed report 3."));
            Assert.That(Blocks().Select(block => (block.Kind, block.Reason)), Is.EqualTo(new[] { (ReportCommand.MuteKind, "chat spam") }));
            Assert.That(test.World.AdminRequests.Single().AccountBlock.AccountId, Is.EqualTo(OffenderAccount));
            Assert.That(Closed(3), Is.True);
        });
    }

    // A second mute is refused by restrict (one record per kind), so that report stays open.
    [Test]
    public void Mute_AlreadyMuted_LeavesTheReportOpen() {
        Assert.That(Run("mute", "3", "1h").Code, Is.EqualTo(0));
        Assert.That(Blocks().Single().Reason, Is.EqualTo("report 3"));
        using (GameStorage.Request db = test.Storage.Context()) {
            db.ReportPlayer(offender, "Renamed", 22, "Other", "again", PlayerReportFlag.Behavior);
        }

        (int code, string[] lines) = Run("mute", "4", "1h");
        Assert.Multiple(() => {
            Assert.That(code, Is.EqualTo(1));
            Assert.That(lines.Single(), Does.StartWith($"Kind {ReportCommand.MuteKind} is already set"));
            Assert.That(Closed(4), Is.False);
        });
    }

    [Test]
    public void Ban_BansTheAccountDisconnectsAndCloses() {
        (int code, string[] lines) = Run("ban", "3", "-d", "7d", "cheating");

        using GameStorage.Request db = test.Storage.Context();
        (bool banned, Maple2.Database.Model.Ban? ban) = db.GetBanStatus(OffenderAccount, null, null);
        Assert.Multiple(() => {
            Assert.That(code, Is.EqualTo(0));
            Assert.That(lines.First(), Does.StartWith("Banned 'offender'"));
            Assert.That(lines.Last(), Is.EqualTo("Closed report 3."));
            Assert.That(banned, Is.True);
            Assert.That(ban!.Reason, Is.EqualTo("cheating"));
            Assert.That(test.World.Disconnects.Single().CharacterId, Is.EqualTo(offender));
            Assert.That(Closed(3), Is.True);
        });
        Assert.That(Run("ban", "3").Lines, Is.EqualTo(new[] { "Report 3 is closed already." }));
    }

    // The reason words reach restrict and ban as the reason only: "-t ip" is not read again as ban's type option,
    // so this is an account ban (an IP ban of an offline character would be refused).
    [Test]
    public void ReasonWords_AreNotReadAsOptions() {
        (int code, string[] lines) = Run("ban", "3", "spam", "-t", "ip");

        using GameStorage.Request db = test.Storage.Context();
        (bool banned, Maple2.Database.Model.Ban? ban) = db.GetBanStatus(OffenderAccount, null, null);
        Assert.Multiple(() => {
            Assert.That(code, Is.EqualTo(0));
            Assert.That(lines.First(), Does.StartWith("Banned 'offender'"));
            Assert.That(banned, Is.True);
            Assert.That(ban!.Reason, Is.EqualTo("spam -t ip"));
            Assert.That(ban.IpAddress, Is.Null);
        });

        using (GameStorage.Request add = test.Storage.Context()) {
            add.ReportPlayer(offender, "Renamed", 22, "Other", "again", PlayerReportFlag.Behavior);
        }
        Assert.That(Run("mute", "4", "1h", "see", "-s", "1h").Code, Is.EqualTo(0));
        Assert.That(Blocks().Single().Reason, Is.EqualTo("see -s 1h"));
    }

    // A reported character on a GM account: restrict and ban both refuse it, so the report stays open.
    [Test]
    public void GmTarget_IsRefusedAndTheReportStaysOpen() {
        const long gmAccount = 3000;
        long gm;
        using (var context = new Ms2Context(test.Options)) {
            Maple2.Database.Model.Account account = new Account { Username = "staff", AdminPermissions = AdminPermissions.GameMaster };
            account.Id = gmAccount;
            context.Account.Add(account);
            context.SaveChanges();
        }
        using (GameStorage.Request db = test.Storage.Context()) {
            gm = db.CreateCharacter(new Character {
                AccountId = gmAccount,
                Name = "Staff",
                Job = Job.Newbie,
                Mastery = new Mastery(),
                MapId = 2000023,
                ReturnMaps = new Maple2.Tools.LimitedStack<int>(3, 2000023),
            })!.Id;
            db.ReportPlayer(gm, "Staff", 21, "Reporter", "abuse", PlayerReportFlag.Behavior);
        }

        (int mute, string[] muteLines) = Run("mute", "4", "1h");
        (int ban, string[] banLines) = Run("ban", "4");
        using GameStorage.Request read = test.Storage.Context();
        Assert.Multiple(() => {
            Assert.That(mute, Is.EqualTo(1));
            Assert.That(muteLines.Single(), Is.EqualTo("Cannot restrict GM/Admin account of 'Staff'."));
            Assert.That(ban, Is.EqualTo(1));
            Assert.That(banLines.Single(), Is.EqualTo("Cannot account ban GM/Admin account 'Staff'."));
            Assert.That(read.GetAccountBlocks(gmAccount), Is.Empty);
            Assert.That(read.GetBanStatus(gmAccount, null, null).IsBanned, Is.False);
            Assert.That(test.World.Disconnects, Is.Empty);
            Assert.That(Closed(4), Is.False);
        });
    }

    // Report 1's character does not exist: nothing is done and the report stays open.
    [Test]
    public void MissingCharacter_DoesNothing() {
        Assert.Multiple(() => {
            Assert.That(Run("ban", "1").Lines, Is.EqualTo(new[] { "Character 11 of report 1 not found." }));
            Assert.That(Run("mute", "1", "1h").Code, Is.EqualTo(1));
            Assert.That(Closed(1), Is.False);
            Assert.That(test.World.Disconnects, Is.Empty);
        });
    }
}
