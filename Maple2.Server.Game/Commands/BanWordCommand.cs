using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.IO;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Validators;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.Commands;

// The live ban-word list (DynamicBanWordRules), empty until a GM adds words. A change goes to every player on every
// channel (DynamicBanword 0x124); a player entering later gets the list at server entry (DynamicBanwordHandler).
public class BanWordCommand : GameCommand {
    public BanWordCommand(GameSession session) : base(AdminPermissions.Ban, "banword", "The chat ban-word list: list, add, remove.") {
        AddCommand(new ListSubcommand(session));
        AddCommand(new AddSubcommand(session));
        AddCommand(new RemoveSubcommand(session));
    }

    internal static string Line(DynamicBanWord word) {
        string action = word.Type switch {
            DynamicBanWordRules.Block => "hides the line",
            _ when word.Replacement.Length == 0 => "masked with *",
            _ => $"replaced by '{word.Replacement}'",
        };
        return $"#{word.Id} '{word.Word}': {action}";
    }

    // Every channel reloads the list and sends it to its players.
    private static void Push(InvocationContext ctx, GameSession session) {
        try {
            AdminResponse response = session.World.Admin(new AdminRequest {
                RequesterId = session.CharacterId,
                BanWord = new AdminRequest.Types.BanWord(),
            });
            if (!string.IsNullOrEmpty(response.Message)) {
                ctx.Console.Out.WriteLine(response.Message);
            }
        } catch (Exception ex) {
            ctx.Console.Out.WriteLine($"Error: {ex.Message}");
        }
    }

    private class ListSubcommand : Command {
        private readonly GameSession session;

        public ListSubcommand(GameSession session) : base("list", "List the ban words.") {
            this.session = session;
            this.SetHandler<InvocationContext>(Handle);
        }

        private void Handle(InvocationContext ctx) {
            using GameStorage.Request db = session.GameStorage.Context();
            IList<DynamicBanWord> words = db.GetDynamicBanWords();
            if (words.Count == 0) {
                ctx.Console.Out.WriteLine("No ban words.");
                return;
            }
            foreach (DynamicBanWord word in words) {
                ctx.Console.Out.WriteLine(Line(word));
            }
        }
    }

    private class AddSubcommand : Command {
        private readonly GameSession session;

        public AddSubcommand(GameSession session) : base("add", "Add a ban word, masked with * unless --block or --replace. Usage: banword add <word> [--block | --replace <text>]") {
            this.session = session;

            var word = new Argument<string>("word", "The word (a phrase in quotes).");
            var block = new Option<bool>(["--block", "-b"], "Hide the whole chat line.");
            var replace = new Option<string>(["--replace", "-r"], () => string.Empty, "Show this text instead of the word.");
            AddArgument(word);
            AddOption(block);
            AddOption(replace);
            this.SetHandler<InvocationContext, string, bool, string>(Handle, word, block, replace);
        }

        private void Handle(InvocationContext ctx, string word, bool block, string replace) {
            if (block && replace.Length > 0) {
                ctx.Console.Out.WriteLine("Use --block or --replace, not both.");
                ctx.ExitCode = 1;
                return;
            }
            int type = block ? DynamicBanWordRules.Block : DynamicBanWordRules.Mask;

            DynamicBanWord? added;
            using (GameStorage.Request db = session.GameStorage.Context()) {
                string? refusal = DynamicBanWordRules.Refusal(word, replace, db.GetDynamicBanWords());
                if (refusal != null) {
                    ctx.Console.Out.WriteLine(refusal);
                    ctx.ExitCode = 1;
                    return;
                }
                added = db.AddDynamicBanWord(type, word, replace, session.AccountId);
            }
            if (added == null) {
                ctx.Console.Out.WriteLine("The word could not be saved.");
                ctx.ExitCode = 1;
                return;
            }
            ctx.Console.Out.WriteLine($"Added {Line(added)}");
            Push(ctx, session);
        }
    }

    private class RemoveSubcommand : Command {
        private readonly GameSession session;

        public RemoveSubcommand(GameSession session) : base("remove", "Remove a ban word by its id (banword list). Usage: banword remove <id>") {
            this.session = session;

            var id = new Argument<int>("id", "The word's id.");
            AddArgument(id);
            this.SetHandler<InvocationContext, int>(Handle, id);
        }

        private void Handle(InvocationContext ctx, int id) {
            bool removed;
            using (GameStorage.Request db = session.GameStorage.Context()) {
                removed = db.RemoveDynamicBanWord(id);
            }
            if (!removed) {
                ctx.Console.Out.WriteLine($"Ban word {id} not found.");
                ctx.ExitCode = 1;
                return;
            }
            ctx.Console.Out.WriteLine($"Removed ban word {id}.");
            Push(ctx, session);
        }
    }
}
