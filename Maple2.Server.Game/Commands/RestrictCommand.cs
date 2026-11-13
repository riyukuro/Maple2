using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.IO;
using Grpc.Core;
using Maple2.Database.Extensions;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Validators;
using Maple2.Server.Game.Session;
using Maple2.Tools;
using Serilog;

namespace Maple2.Server.Game.Commands;

// Account restrictions (AdminBlock 0x62, the client's CBlockChecker kinds 1..10). The client enforces each kind's
// actions itself; the server refuses chat for the kinds that block it (2 Penalty, 6). Kinds 1 and 9 block the login,
// which is the ban command's job.
public class RestrictCommand : GameCommand {
    private static readonly ILogger Logger = Log.Logger.ForContext<RestrictCommand>();

    public RestrictCommand(GameSession session) : base(AdminPermissions.Ban, "restrict", "Account restrictions (chat, UGC, mail...).") {
        AddCommand(new AddSubcommand(session));
        AddCommand(new RemoveSubcommand(session));
        AddCommand(new ExtendSubcommand(session));
        AddCommand(new ListSubcommand(session));
    }

    // A GM (the client's GM level) or an account that may restrict or ban others itself.
    private static bool IsStaff(Account account) => AdminLevel.IsGm(account.AdminPermissions) || account.AdminPermissions.HasFlag(AdminPermissions.Ban);

    private static bool ReasonFits(InvocationContext ctx, string reason) {
        if (reason.Length <= AccountBlockRules.MaxReasonLength) {
            return true;
        }
        ctx.Console.Out.WriteLine($"Reason is {reason.Length} characters; at most {AccountBlockRules.MaxReasonLength}.");
        return false;
    }

    // Tells the channel holding the account (any channel) to reload and update the client.
    private static void Push(GameSession session, long accountId, int mode, AccountBlock block, int delta = 0) {
        try {
            session.World.Admin(new AdminRequest {
                RequesterId = session.CharacterId,
                AccountBlock = new AdminRequest.Types.AccountBlock {
                    AccountId = accountId,
                    Mode = mode,
                    BlockId = block.Id,
                    Kind = block.Kind,
                    ExpiryTime = block.ExpiryTime,
                    Delta = delta,
                },
            });
        } catch (RpcException ex) {
            Logger.Error(ex, "Failed to push restriction {BlockId} to account {AccountId}", block.Id, accountId);
        }
    }

    private class AddSubcommand : Command {
        private readonly GameSession session;

        public AddSubcommand(GameSession session) : base("add", "Restrict an account. Usage: restrict add <character> <kind> <duration> [reason...]") {
            this.session = session;

            var character = new Argument<string>("character", "Target character name.");
            var kind = new Argument<int>("kind", "Kind 2..8 or 10: 2 Penalty (chat, trade, UGC...), 3, 4 Citation (UGC), 5, 6 chat, 7 mail, 8, 10.");
            var duration = new Argument<string>("duration", "Duration, e.g. 30m, 12h, 7d.");
            var reason = new Argument<string[]>("reason", () => [], "Reason words.");
            AddArgument(character);
            AddArgument(kind);
            AddArgument(duration);
            AddArgument(reason);
            this.SetHandler<InvocationContext, string, int, string, string[]>(Handle, character, kind, duration, reason);
        }

        private void Handle(InvocationContext ctx, string characterName, int kind, string durationToken, string[] reasonWords) {
            if (!AccountBlockRules.GmKind(kind)) {
                ctx.Console.Out.WriteLine($"Invalid kind {kind}: use 2..8 or 10 (kinds 1 and 9 block the login: use ban).");
                return;
            }
            if (!DurationParser.TryParse(durationToken, out TimeSpan duration)) {
                ctx.Console.Out.WriteLine($"Invalid duration '{durationToken}' (e.g. 30m, 12h, 7d).");
                return;
            }
            DateTime now = DateTime.UtcNow;
            if (!AccountBlockRules.ExpiryFits(now.ToEpochSeconds() + (long) duration.TotalSeconds)) {
                ctx.Console.Out.WriteLine($"Duration '{durationToken}' ends after the year 9999.");
                return;
            }
            string reason = string.Join(' ', reasonWords);
            if (!ReasonFits(ctx, reason)) {
                return;
            }

            using GameStorage.Request db = session.GameStorage.Context();
            Account? target = db.GetAccountByCharacterName(characterName);
            if (target == null) {
                ctx.Console.Out.WriteLine($"Character '{characterName}' not found.");
                return;
            }
            if (IsStaff(target)) {
                ctx.Console.Out.WriteLine($"Cannot restrict GM/Admin account of '{characterName}'.");
                return;
            }
            // The client keeps one record per kind.
            AccountBlock? existing = db.GetAccountBlocks(target.Id).FirstOrDefault(block => block.Kind == kind);
            if (existing != null) {
                ctx.Console.Out.WriteLine($"Kind {kind} is already set (id {existing.Id}); use restrict extend.");
                return;
            }

            AccountBlock? created = db.CreateAccountBlock(target.Id, kind, reason, now.Add(duration), session.AccountId);
            if (created == null) {
                ctx.Console.Out.WriteLine("Failed to save the restriction.");
                return;
            }

            Logger.Information("Restrict: GM {GmAccountId} ({GmName}) restricted account {AccountId} ({Character}) kind {Kind} until {Expiry} id {BlockId} reason '{Reason}'",
                session.AccountId, session.PlayerName, target.Id, characterName, kind, created.ExpiryTime.FromEpochSeconds(), created.Id, reason);
            Push(session, target.Id, 0, created);
            ctx.Console.Out.WriteLine($"Restricted '{characterName}' kind {kind} until {created.ExpiryTime.FromEpochSeconds():yyyy-MM-dd HH:mm:ss} (id {created.Id}).");
        }
    }

    private class RemoveSubcommand : Command {
        private readonly GameSession session;

        public RemoveSubcommand(GameSession session) : base("remove", "Lift a restriction. Usage: restrict remove <id> [reason...]") {
            this.session = session;

            var id = new Argument<long>("id", "Restriction id (restrict list).");
            var reason = new Argument<string[]>("reason", () => [], "Reason words.");
            AddArgument(id);
            AddArgument(reason);
            this.SetHandler<InvocationContext, long, string[]>(Handle, id, reason);
        }

        private void Handle(InvocationContext ctx, long id, string[] reasonWords) {
            using GameStorage.Request db = session.GameStorage.Context();
            AccountBlock? removed = db.RemoveAccountBlock(id);
            if (removed == null) {
                ctx.Console.Out.WriteLine($"Restriction {id} not found.");
                return;
            }

            string reason = string.Join(' ', reasonWords);
            Logger.Information("Restrict: GM {GmAccountId} ({GmName}) lifted restriction {BlockId} of account {AccountId} kind {Kind} (was until {Expiry}) reason '{Reason}'",
                session.AccountId, session.PlayerName, removed.Id, removed.AccountId, removed.Kind, removed.ExpiryTime.FromEpochSeconds(), reason);
            Push(session, removed.AccountId, 1, removed);
            ctx.Console.Out.WriteLine($"Lifted restriction {id} (kind {removed.Kind}).");
        }
    }

    private class ExtendSubcommand : Command {
        private readonly GameSession session;

        public ExtendSubcommand(GameSession session) : base("extend", "Lengthen or shorten a restriction. Usage: restrict extend <id> <duration> [--shorten] [reason...]") {
            this.session = session;

            var id = new Argument<long>("id", "Restriction id (restrict list).");
            var duration = new Argument<string>("duration", "Change, e.g. 1d or 2h.");
            var shorten = new Option<bool>(["--shorten", "-s"], "Shorten by the duration instead.");
            var reason = new Argument<string[]>("reason", () => [], "Reason words.");
            AddArgument(id);
            AddArgument(duration);
            AddOption(shorten);
            AddArgument(reason);
            this.SetHandler<InvocationContext, long, string, bool, string[]>(Handle, id, duration, shorten, reason);
        }

        private void Handle(InvocationContext ctx, long id, string durationToken, bool shorten, string[] reasonWords) {
            if (!DurationParser.TryParse(durationToken, out TimeSpan duration) || duration.TotalSeconds > int.MaxValue) {
                ctx.Console.Out.WriteLine($"Invalid duration '{durationToken}' (e.g. 1d, 2h).");
                return;
            }
            int delta = (int) (shorten ? -duration.TotalSeconds : duration.TotalSeconds);

            using GameStorage.Request db = session.GameStorage.Context();
            AccountBlock? block = db.GetAccountBlock(id);
            if (block == null) {
                ctx.Console.Out.WriteLine($"Restriction {id} not found or ended.");
                return;
            }
            Account? target = db.GetAccount(block.AccountId);
            if (target != null && IsStaff(target)) {
                ctx.Console.Out.WriteLine("Cannot change a GM/Admin account's restriction.");
                return;
            }

            if (!AccountBlockRules.ExpiryFits(block.ExpiryTime + delta)) {
                ctx.Console.Out.WriteLine($"Restriction {id} would end after the year 9999.");
                return;
            }

            AccountBlock? changed = db.SetAccountBlockExpiry(id, block.ExpiryTime + delta);
            if (changed == null) {
                ctx.Console.Out.WriteLine("Failed to save the restriction.");
                return;
            }

            string reason = string.Join(' ', reasonWords);
            Logger.Information("Restrict: GM {GmAccountId} ({GmName}) changed restriction {BlockId} of account {AccountId} kind {Kind} by {Delta}s to {Expiry} reason '{Reason}'",
                session.AccountId, session.PlayerName, changed.Id, changed.AccountId, changed.Kind, delta, changed.ExpiryTime.FromEpochSeconds(), reason);
            Push(session, changed.AccountId, 2, changed, delta);
            ctx.Console.Out.WriteLine($"Restriction {id} now ends {changed.ExpiryTime.FromEpochSeconds():yyyy-MM-dd HH:mm:ss}.");
        }
    }

    private class ListSubcommand : Command {
        private readonly GameSession session;

        public ListSubcommand(GameSession session) : base("list", "List an account's live restrictions. Usage: restrict list <character>") {
            this.session = session;

            var character = new Argument<string>("character", "Target character name.");
            AddArgument(character);
            this.SetHandler<InvocationContext, string>(Handle, character);
        }

        private void Handle(InvocationContext ctx, string characterName) {
            using GameStorage.Request db = session.GameStorage.Context();
            Account? target = db.GetAccountByCharacterName(characterName);
            if (target == null) {
                ctx.Console.Out.WriteLine($"Character '{characterName}' not found.");
                return;
            }

            IList<AccountBlock> blocks = db.GetAccountBlocks(target.Id);
            if (blocks.Count == 0) {
                ctx.Console.Out.WriteLine("No live restrictions.");
                return;
            }
            foreach (AccountBlock block in blocks) {
                ctx.Console.Out.WriteLine($"id {block.Id} kind {block.Kind} {block.StartTime.FromEpochSeconds():yyyy-MM-dd HH:mm:ss} - {block.ExpiryTime.FromEpochSeconds():yyyy-MM-dd HH:mm:ss} '{block.Reason}'");
            }
        }
    }
}
