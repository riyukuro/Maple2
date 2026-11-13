using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.IO;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Server.Game.Session;
using Serilog;

namespace Maple2.Server.Game.Commands;

// The reports players send (player-reports). A GM lists, reads and closes them, or mutes or bans the reported
// character through the restrict and ban commands. Moderation, so it needs the Ban permission, as those do.
public class ReportCommand : GameCommand {
    private static readonly ILogger Logger = Log.Logger.ForContext<ReportCommand>();
    public const int DefaultCount = 20;
    public const int MaxCount = 100;

    public ReportCommand(GameSession session) : base(AdminPermissions.Ban, "report", "Player reports: list, read, close, mute, ban.") {
        AddCommand(new ListSubcommand(session));
        AddCommand(new ReadSubcommand(session));
        AddCommand(new CloseSubcommand(session));
        AddCommand(new MuteSubcommand(session));
        AddCommand(new BanSubcommand(session));
    }

    // The restriction kind a mute sets: kind 6 blocks chat alone (restrict).
    public const int MuteKind = 6;

    private static string Line(PlayerReportEntry report) {
        string closed = report.CloseTime == null ? string.Empty : " (closed)";
        return $"#{report.Id} {report.CreateTime:yyyy-MM-dd HH:mm} {report.Category} '{report.PlayerName}' by '{report.ReporterName}': {report.Detail}{closed}";
    }

    private class ListSubcommand : Command {
        private readonly GameSession session;

        public ListSubcommand(GameSession session) : base("list", $"List the newest open reports. Usage: report list [--all] [-n count] (count 1..{MaxCount}, default {DefaultCount})") {
            this.session = session;

            var all = new Option<bool>(["--all", "-a"], "Closed reports too.");
            var count = new Option<int>(["--count", "-n"], () => DefaultCount, "How many.");
            AddOption(all);
            AddOption(count);
            this.SetHandler<InvocationContext, bool, int>(Handle, all, count);
        }

        private void Handle(InvocationContext ctx, bool all, int count) {
            if (count < 1 || count > MaxCount) {
                ctx.Console.Out.WriteLine($"Count must be 1..{MaxCount}.");
                ctx.ExitCode = 1;
                return;
            }

            using GameStorage.Request db = session.GameStorage.Context();
            IList<PlayerReportEntry> reports = db.GetPlayerReports(all, count);
            if (reports.Count == 0) {
                ctx.Console.Out.WriteLine(all ? "No reports." : "No open reports.");
                return;
            }
            foreach (PlayerReportEntry report in reports) {
                ctx.Console.Out.WriteLine(Line(report));
            }
        }
    }

    private class ReadSubcommand : Command {
        private readonly GameSession session;

        public ReadSubcommand(GameSession session) : base("read", "Show one report. Usage: report read <id>") {
            this.session = session;

            var id = new Argument<long>("id", "Report id (report list).");
            AddArgument(id);
            this.SetHandler<InvocationContext, long>(Handle, id);
        }

        private void Handle(InvocationContext ctx, long id) {
            using GameStorage.Request db = session.GameStorage.Context();
            PlayerReportEntry? report = db.GetPlayerReport(id);
            if (report == null) {
                ctx.Console.Out.WriteLine($"Report {id} not found.");
                ctx.ExitCode = 1;
                return;
            }

            ctx.Console.Out.WriteLine(Line(report));
            ctx.Console.Out.WriteLine($"Reported: '{report.PlayerName}' (character {report.CharacterId})");
            ctx.Console.Out.WriteLine($"Reporter: '{report.ReporterName}' (character {report.ReporterCharacterId})");
            ctx.Console.Out.WriteLine($"Reason: {report.Reason}");
            if (report.CloseTime != null) {
                ctx.Console.Out.WriteLine($"Closed {report.CloseTime:yyyy-MM-dd HH:mm} by account {report.ClosedBy}");
            }
        }
    }

    private class CloseSubcommand : Command {
        private readonly GameSession session;

        public CloseSubcommand(GameSession session) : base("close", "Close a report. Usage: report close <id> [note...]") {
            this.session = session;

            var id = new Argument<long>("id", "Report id (report list).");
            var note = new Argument<string[]>("note", () => [], "Note words, for the log.");
            AddArgument(id);
            AddArgument(note);
            this.SetHandler<InvocationContext, long, string[]>(Handle, id, note);
        }

        private void Handle(InvocationContext ctx, long id, string[] noteWords) {
            using GameStorage.Request db = session.GameStorage.Context();
            if (!Close(ctx, db, session, id, string.Join(' ', noteWords))) {
                ctx.ExitCode = 1;
            }
        }
    }

    internal static bool Close(InvocationContext ctx, GameStorage.Request db, GameSession session, long id, string note) {
        PlayerReportEntry? closed = db.ClosePlayerReport(id, session.AccountId);
        if (closed == null) {
            PlayerReportEntry? report = db.GetPlayerReport(id);
            ctx.Console.Out.WriteLine(report == null ? $"Report {id} not found."
                : report.CloseTime != null ? $"Report {id} is closed already." : $"Failed to close report {id}.");
            return false;
        }

        Logger.Information("Report: GM {GmAccountId} ({GmName}) closed report {ReportId} on {Character} ({CharacterId}) note '{Note}'",
            session.AccountId, session.PlayerName, id, closed.PlayerName, closed.CharacterId, note);
        ctx.Console.Out.WriteLine($"Closed report {id}.");
        return true;
    }

    // The open report and its character's current name and account; null (with the answer printed) otherwise.
    private static (PlayerReportEntry Report, string Name, long AccountId)? Target(InvocationContext ctx, GameSession session, long id) {
        using GameStorage.Request db = session.GameStorage.Context();
        PlayerReportEntry? report = db.GetPlayerReport(id);
        if (report == null || report.CloseTime != null) {
            ctx.Console.Out.WriteLine(report == null ? $"Report {id} not found." : $"Report {id} is closed already.");
            return null;
        }
        Character? character = db.GetCharacter(report.CharacterId);
        if (character == null) {
            ctx.Console.Out.WriteLine($"Character {report.CharacterId} of report {id} not found.");
            return null;
        }
        return (report, character.Name, character.AccountId);
    }

    private static string Reason(long id, string[] words) => words.Length == 0 ? $"report {id}" : string.Join(' ', words);

    private class MuteSubcommand : Command {
        private readonly GameSession session;

        public MuteSubcommand(GameSession session) : base("mute", "Mute the reported character's account (restrict kind 6) and close the report. Usage: report mute <id> <duration> [reason...]") {
            this.session = session;

            var id = new Argument<long>("id", "Report id (report list).");
            var duration = new Argument<string>("duration", "Duration, e.g. 30m, 12h, 7d.");
            var reason = new Argument<string[]>("reason", () => [], "Reason words (default: report <id>).");
            AddArgument(id);
            AddArgument(duration);
            AddArgument(reason);
            this.SetHandler<InvocationContext, long, string, string[]>(Handle, id, duration, reason);
        }

        private void Handle(InvocationContext ctx, long id, string duration, string[] reasonWords) {
            if (Target(ctx, session, id) is not { } target) {
                ctx.ExitCode = 1;
                return;
            }
            HashSet<long> before = Mutes(target.AccountId);
            new RestrictCommand(session).Invoke(["add", target.Name, MuteKind.ToString(), duration, .. Reason(id, reasonWords).Split(' ')], ctx.Console);
            // The report is closed only for a new mute: restrict refuses a second one of the kind.
            if (!Mutes(target.AccountId).Except(before).Any()) {
                ctx.ExitCode = 1;
                return;
            }
            using GameStorage.Request db = session.GameStorage.Context();
            if (!Close(ctx, db, session, id, $"muted {duration}")) {
                ctx.ExitCode = 1;
            }
        }

        private HashSet<long> Mutes(long accountId) {
            using GameStorage.Request db = session.GameStorage.Context();
            return db.GetAccountBlocks(accountId).Where(block => block.Kind == MuteKind).Select(block => block.Id).ToHashSet();
        }
    }

    private class BanSubcommand : Command {
        private readonly GameSession session;

        public BanSubcommand(GameSession session) : base("ban", "Ban the reported character's account (ban) and close the report. Usage: report ban <id> [-d days] [reason...]") {
            this.session = session;

            var id = new Argument<long>("id", "Report id (report list).");
            var duration = new Option<string>(["--duration", "-d"], () => string.Empty, "Duration in days (e.g. 1d, 7d). Omit for permanent.");
            var reason = new Argument<string[]>("reason", () => [], "Reason words (default: report <id>).");
            AddArgument(id);
            AddOption(duration);
            AddArgument(reason);
            this.SetHandler<InvocationContext, long, string, string[]>(Handle, id, duration, reason);
        }

        private void Handle(InvocationContext ctx, long id, string duration, string[] reasonWords) {
            if (Target(ctx, session, id) is not { } target) {
                ctx.ExitCode = 1;
                return;
            }
            if (Banned(target.AccountId)) {
                ctx.Console.Out.WriteLine($"Account of '{target.Name}' is banned already.");
                ctx.ExitCode = 1;
                return;
            }
            string[] args = duration.Length == 0 ? [target.Name] : [target.Name, "-d", duration];
            new BanCommand(session).Invoke([.. args, .. Reason(id, reasonWords).Split(' ')], ctx.Console);
            if (!Banned(target.AccountId)) {
                ctx.ExitCode = 1;
                return;
            }
            using GameStorage.Request db = session.GameStorage.Context();
            if (!Close(ctx, db, session, id, duration.Length == 0 ? "banned" : $"banned {duration}")) {
                ctx.ExitCode = 1;
            }
        }

        private bool Banned(long accountId) {
            using GameStorage.Request db = session.GameStorage.Context();
            return db.GetBanStatus(accountId, null, null).IsBanned;
        }
    }
}
