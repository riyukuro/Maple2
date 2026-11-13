using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.IO;
using Maple2.Model.Enum;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.Commands;

// Shutdown and maintenance notices to every channel (ShutdownNotice 0xAF). Notices only: no server is stopped.
public class ShutdownCommand : GameCommand {
    public ShutdownCommand(GameSession session) : base(AdminPermissions.Admin, "shutdown", "Shutdown and maintenance notices to all players (no server is stopped).") {
        AddCommand(new TimeCommand(session));
        AddCommand(new CancelCommand(session));
        AddCommand(new MessageCommand(session, "quit", 1, "A message box, then the players' clients quit (GMs get a notice)."));
        AddCommand(new MessageCommand(session, "maintenance", 2, "A maintenance box, then the players' clients quit (GMs get a notice)."));
        AddCommand(new MessageCommand(session, "notice", 3, "A system notice."));
    }

    private static void Send(InvocationContext ctx, GameSession session, AdminRequest.Types.ShutdownNotice notice, string done) {
        try {
            AdminResponse response = session.World.Admin(new AdminRequest {
                RequesterId = session.CharacterId,
                ShutdownNotice = notice,
            });
            if (!string.IsNullOrEmpty(response.Message)) {
                ctx.Console.Out.WriteLine(response.Message);
                return;
            }
        } catch (Exception ex) {
            ctx.Console.Out.WriteLine($"Error: {ex.Message}");
            return;
        }
        ctx.Console.Out.WriteLine(done);
    }

    private class TimeCommand : Command {
        private readonly GameSession session;

        public TimeCommand(GameSession session) : base("time", "Announce a shutdown in <minutes>; clients post notices 30, 15 and 5 minutes before.") {
            this.session = session;

            var minutes = new Argument<int>("minutes", "Minutes until the shutdown.");
            AddArgument(minutes);
            this.SetHandler<InvocationContext, int>(Handle, minutes);
        }

        private void Handle(InvocationContext ctx, int minutes) {
            if (minutes <= 0) {
                ctx.Console.Out.WriteLine("Minutes must be positive.");
                return;
            }
            DateTimeOffset time = DateTimeOffset.UtcNow.AddMinutes(minutes);
            Send(ctx, session, new AdminRequest.Types.ShutdownNotice {
                Kind = 0,
                Time = time.ToUnixTimeSeconds(),
            }, $"Shutdown announced for {time.ToLocalTime():yyyy-MM-dd HH:mm}.");
        }
    }

    private class CancelCommand : Command {
        private readonly GameSession session;

        public CancelCommand(GameSession session) : base("cancel", "Stop the shutdown countdown notices.") {
            this.session = session;
            this.SetHandler<InvocationContext>(Handle);
        }

        private void Handle(InvocationContext ctx) {
            Send(ctx, session, new AdminRequest.Types.ShutdownNotice {
                Kind = 0,
                Time = 0,
            }, "Shutdown notices stopped.");
        }
    }

    private class MessageCommand : Command {
        private readonly GameSession session;
        private readonly int kind;

        public MessageCommand(GameSession session, string name, int kind, string description) : base(name, description) {
            this.session = session;
            this.kind = kind;

            var message = new Argument<string[]>("message", Array.Empty<string>, "The message.");
            AddArgument(message);
            this.SetHandler<InvocationContext, string[]>(Handle, message);
        }

        private void Handle(InvocationContext ctx, string[] message) {
            string text = string.Join(" ", message);
            if (string.IsNullOrEmpty(text)) {
                ctx.Console.Out.WriteLine("Message cannot be empty.");
                return;
            }
            Send(ctx, session, new AdminRequest.Types.ShutdownNotice {
                Kind = kind,
                Message = text,
            }, "Sent.");
        }
    }
}
