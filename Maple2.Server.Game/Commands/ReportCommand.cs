using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.IO;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Server.Game.Session;
using Serilog;

namespace Maple2.Server.Game.Commands;

// The reports players send (player-reports). A GM lists, reads and closes them. Moderation, so it needs the Ban
// permission, as restrict and ban do.
public class ReportCommand : GameCommand {
    private static readonly ILogger Logger = Log.Logger.ForContext<ReportCommand>();
    public const int DefaultCount = 20;
    public const int MaxCount = 100;

    public ReportCommand(GameSession session) : base(AdminPermissions.Ban, "report", "Player reports: list, read, close.") {
        AddCommand(new ListSubcommand(session));
        AddCommand(new ReadSubcommand(session));
        AddCommand(new CloseSubcommand(session));
    }

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
}
