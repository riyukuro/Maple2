using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.IO;
using Maple2.Model.Enum;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.Commands;

// Login refusal for maintenance (login result 11: the message under the client's maintenance title). Kept by the
// World server; GMs still log in. Players already in the game stay.
public class MaintenanceCommand : GameCommand {
    public MaintenanceCommand(GameSession session) : base(AdminPermissions.Admin, "maintenance", "Refuse player logins with a maintenance message (GMs still log in).") {
        AddCommand(new OnCommand(session));
        AddCommand(new ActionCommand(session, "off", 0, "Allow player logins again."));
        AddCommand(new ActionCommand(session, "status", 2, "Show whether logins are refused."));
    }

    private static void Send(InvocationContext ctx, GameSession session, int action, string message) {
        try {
            AdminResponse response = session.World.Admin(new AdminRequest {
                RequesterId = session.CharacterId,
                Maintenance = new AdminRequest.Types.Maintenance {
                    Action = action,
                    Message = message,
                },
            });
            if (response.Error != 0) {
                ctx.Console.Out.WriteLine($"Error: {response.Message}");
                return;
            }
            ctx.Console.Out.WriteLine(string.IsNullOrEmpty(response.Maintenance)
                ? "Logins are open."
                : $"Player logins are refused: {response.Maintenance}");
        } catch (Exception ex) {
            ctx.Console.Out.WriteLine($"Error: {ex.Message}");
        }
    }

    private class OnCommand : Command {
        private readonly GameSession session;

        public OnCommand(GameSession session) : base("on", "Refuse player logins, showing <message>.") {
            this.session = session;

            var message = new Argument<string[]>("message", Array.Empty<string>, "The message shown at login.");
            AddArgument(message);
            this.SetHandler<InvocationContext, string[]>(Handle, message);
        }

        private void Handle(InvocationContext ctx, string[] message) {
            string text = string.Join(" ", message).Trim();
            if (text.Length == 0) {
                ctx.Console.Out.WriteLine("Message cannot be empty.");
                return;
            }
            Send(ctx, session, 1, text);
        }
    }

    private class ActionCommand : Command {
        private readonly GameSession session;
        private readonly int action;

        public ActionCommand(GameSession session, string name, int action, string description) : base(name, description) {
            this.session = session;
            this.action = action;
            this.SetHandler<InvocationContext>(Handle);
        }

        private void Handle(InvocationContext ctx) {
            Send(ctx, session, action, string.Empty);
        }
    }
}
