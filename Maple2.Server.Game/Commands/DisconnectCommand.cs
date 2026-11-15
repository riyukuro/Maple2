using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.IO;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.Commands;

public class DisconnectCommand : GameCommand {
    private readonly GameSession session;

    public DisconnectCommand(GameSession session) : base(AdminPermissions.Admin, "disconnect", "Disconnect players") {
        this.session = session;
        AddAlias("dc");

        var name = new Argument<string>("characterName", () => string.Empty, "The name of the character to disconnect") {
            Arity = ArgumentArity.ZeroOrOne,
        };
        var allOption = new Option<bool>(["--all", "-a"], () => false, "Disconnect all players.");

        AddArgument(name);
        AddOption(allOption);

        this.SetHandler<InvocationContext, string, bool>(Handle, name, allOption);
    }

    private void Handle(InvocationContext ctx, string playerName, bool all) {
        if (all) {
            ctx.Console.Out.WriteLine("Disconnecting all players...");
            Kick(new DisconnectRequest {
                CharacterId = 0,
                All = true,
            }, null, "Failed to disconnect all players.");
            ctx.ExitCode = 0;
            return;
        }

        if (string.IsNullOrWhiteSpace(playerName)) {
            ctx.Console.Out.WriteLine("Please specify a player name to disconnect.");
            ctx.ExitCode = 1;
            return;
        }

        using GameStorage.Request db = session.GameStorage.Context();
        long characterId = db.GetCharacterId(playerName);
        if (characterId == 0) {
            ctx.Console.Out.WriteLine($"Player '{playerName}' not found.");
            return;
        }

        ctx.Console.Out.WriteLine($"Disconnecting player '{playerName}'...");
        Kick(new DisconnectRequest {
            CharacterId = characterId,
        }, $"Disconnected player '{playerName}'.", $"Failed to disconnect player '{playerName}'.");
        ctx.ExitCode = 0;
    }

    // The kick runs off the field's thread (GameSession.Kick); the GM is told its outcome once the World answers.
    private void Kick(DisconnectRequest request, string? done, string failed) {
        session.Kick(request).ContinueWith(kick => {
            string? line = kick.Result is { Success: true } ? done : failed;
            if (line != null) {
                new GameConsole(session).Out.WriteLine(line);
            }
        }, TaskScheduler.Default);
    }
}
