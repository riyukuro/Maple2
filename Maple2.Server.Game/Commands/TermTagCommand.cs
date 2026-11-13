using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.IO;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Game.Commands;

// "termtag": starts or ends a term of the TermTag table on this channel (TermTagPacket start/end to its players; the
// players who enter later get it in their load). The client opens term-gated content, e.g. the nurturing cubes of
// the NurturingQuestTag constant, only while the term runs. A started term is ended in the online clients when its
// end passes (GameServer.SendTermEdges), the same as "termtag end". Names match in any case here.
public class TermTagCommand : GameCommand {
    public TermTagCommand(GameSession session) : base(AdminPermissions.EventManagement, "termtag", "Start or end an event term on this channel.") {
        AddCommand(new ListCommand(session));
        AddCommand(new StartCommand(session));
        AddCommand(new EndCommand(session));
    }

    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    private class ListCommand : Command {
        private readonly GameSession session;

        public ListCommand(GameSession session) : base("list", "List the running terms.") {
            this.session = session;
            this.SetHandler<InvocationContext>(Handle);
        }

        private void Handle(InvocationContext ctx) {
            (_, List<TermTagTable.Entry> running) = session.TermTags.Split(session.ServerTableMetadata.TermTagTable, Now());
            ctx.Console.Out.WriteLine(running.Count == 0 ? "No term is running."
                : string.Join(", ", running.Select(entry => $"{entry.Name} ({entry.Id})")));
        }
    }

    private class StartCommand : Command {
        private readonly GameSession session;

        public StartCommand(GameSession session) : base("start", "Start a term now, for <minutes> (default 1440).") {
            this.session = session;

            var name = new Argument<string>("name", "The term name, e.g. NurturingGhostCats.");
            var minutes = new Argument<int>("minutes", () => 1440, "Minutes until the term ends.");
            AddArgument(name);
            AddArgument(minutes);
            this.SetHandler<InvocationContext, string, int>(Handle, name, minutes);
        }

        private void Handle(InvocationContext ctx, string name, int minutes) {
            if (minutes <= 0) {
                ctx.Console.Out.WriteLine("Minutes must be positive.");
                return;
            }
            TermTagTable.Entry? entry = session.TermTags.Find(session.ServerTableMetadata.TermTagTable, name, StringComparison.OrdinalIgnoreCase);
            if (entry == null) {
                ctx.Console.Out.WriteLine($"No term {name} in the TermTag table.");
                return;
            }
            long now = Now();
            TermTagTable.Entry started = entry with { Start = now, End = now + minutes * 60L };
            session.TermTags.Set(started, now);
            // The client's start does not replace a running term it holds (TermTagManagerCli::OnStart inserts,
            // unit_140ad5f00_termtagmanagercli.cpp:62), so a restart ends the old one first (OnEnd erases it, :76).
            if (TermTagState.Running(entry, now)) {
                session.BroadcastChannel(TermTagPacket.End([entry]));
            }
            session.BroadcastChannel(TermTagPacket.Start([started]));
            ctx.Console.Out.WriteLine($"Started {started.Name} ({started.Id}) for {minutes} minutes on this channel.");
        }
    }

    private class EndCommand : Command {
        private readonly GameSession session;

        public EndCommand(GameSession session) : base("end", "End a running term now.") {
            this.session = session;

            var name = new Argument<string>("name", "The term name.");
            AddArgument(name);
            this.SetHandler<InvocationContext, string>(Handle, name);
        }

        private void Handle(InvocationContext ctx, string name) {
            long now = Now();
            TermTagTable.Entry? entry = session.TermTags.Find(session.ServerTableMetadata.TermTagTable, name, StringComparison.OrdinalIgnoreCase);
            if (entry == null || !TermTagState.Running(entry, now)) {
                ctx.Console.Out.WriteLine($"No running term {name}.");
                return;
            }
            TermTagTable.Entry ended = entry with { End = now };
            session.TermTags.Set(ended, now);
            session.BroadcastChannel(TermTagPacket.End([ended]));
            ctx.Console.Out.WriteLine($"Ended {ended.Name} ({ended.Id}) on this channel.");
        }
    }
}
