using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.IO;
using Maple2.Model.Enum;
using Maple2.Model.Validators;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.Commands;

// The notice config (NoticeRules), kept by the world server: the field notice banner (NoticeDialog 0xCF) and the
// lobby's login notice (a LoginNotice game event, which opens the notice that sends NoticeDialog 0x7A).
public class NoticeDialogCommand : GameCommand {
    public NoticeDialogCommand(GameSession session) : base(AdminPermissions.EventManagement, "noticedialog", "The notice banner and the login notice.") {
        AddCommand(new KindCommand(session, "banner", NoticeRules.Banner,
            "The notice banner at each player's first field entry: on, off, or show (to every player now).", "on", "off", "show"));
        AddCommand(new KindCommand(session, "login", NoticeRules.Login, "The login notice in the lobby: on or off.", "on", "off"));
        AddCommand(new StatusCommand(session));
    }

    internal static int? Action(string word) => word.ToLowerInvariant() switch {
        "off" => NoticeRules.Off,
        "on" => NoticeRules.On,
        "show" => NoticeRules.Show,
        _ => null,
    };

    internal static string Describe(bool banner, bool login) =>
        $"Notice banner {(banner ? "on" : "off")}, login notice {(login ? "on" : "off")}.";

    private static void Send(InvocationContext ctx, GameSession session, int kind, int action) {
        try {
            AdminResponse response = session.World.Admin(new AdminRequest {
                RequesterId = session.CharacterId,
                Notice = new AdminRequest.Types.Notice {
                    Kind = kind,
                    Action = action,
                },
            });
            if (!string.IsNullOrEmpty(response.Message)) {
                ctx.Console.Out.WriteLine(response.Message);
            }
            ctx.Console.Out.WriteLine(Describe(response.NoticeBanner, response.NoticeLogin));
        } catch (Exception ex) {
            ctx.Console.Out.WriteLine($"Error: {ex.Message}");
        }
    }

    private class KindCommand : Command {
        private readonly GameSession session;
        private readonly int kind;
        private readonly string[] words;

        public KindCommand(GameSession session, string name, int kind, string description, params string[] words) : base(name, description) {
            this.session = session;
            this.kind = kind;
            this.words = words;

            var action = new Argument<string>("action", string.Join(", ", words));
            AddArgument(action);
            this.SetHandler<InvocationContext, string>(Handle, action);
        }

        private void Handle(InvocationContext ctx, string word) {
            int? action = Action(word);
            if (action == null || !words.Contains(word.ToLowerInvariant())) {
                ctx.Console.Out.WriteLine($"Use one of: {string.Join(", ", words)}.");
                return;
            }
            Send(ctx, session, kind, action.Value);
        }
    }

    private class StatusCommand : Command {
        private readonly GameSession session;

        public StatusCommand(GameSession session) : base("status", "Show the notice config.") {
            this.session = session;
            this.SetHandler<InvocationContext>(Handle);
        }

        private void Handle(InvocationContext ctx) {
            Send(ctx, session, NoticeRules.Banner, NoticeRules.Status);
        }
    }
}
