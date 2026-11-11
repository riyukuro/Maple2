using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.IO;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.Commands;

// The chat commands the client's own GM windows and hotkeys send (the client sends "/Name args" as normal
// chat text). The router lowers the name and registers a command only for an account that holds its
// permission, so each of these is checked on the server like every other command.

// "/MoveField <id>" from the GM map window and "/MoveField <id> 1" from a click on the world map.
public class MoveFieldCommand : GameCommand {
    private readonly GameSession session;
    private readonly MapMetadataStorage mapStorage;

    public MoveFieldCommand(GameSession session, MapMetadataStorage mapStorage) : this(session, mapStorage, "movefield", "Move to a field (the client's GM map window).") { }

    protected MoveFieldCommand(GameSession session, MapMetadataStorage mapStorage, string name, string description) : base(AdminPermissions.Warp, name, description) {
        this.session = session;
        this.mapStorage = mapStorage;

        var mapId = new Argument<int>("id", "Id of the field.");
        var flag = new Argument<int>("flag", () => 0, "Sent as 1 by the world map; not used.");

        AddArgument(mapId);
        AddArgument(flag);
        this.SetHandler<InvocationContext, int, int>(Handle, mapId, flag);
    }

    private void Handle(InvocationContext ctx, int mapId, int flag) {
        if (!mapStorage.TryGet(mapId, out MapMetadata? map)) {
            ctx.Console.Error.WriteLine($"Invalid Map: {mapId}");
            return;
        }

        session.Send(session.PrepareField(map.Id)
            ? FieldEnterPacket.Request(session.Player)
            : FieldEnterPacket.Error(MigrationError.s_move_err_default));
        ctx.ExitCode = 0;
    }
}

// "/AddField <id>" from the GM map window. Handled as a move to the field.
public class AddFieldCommand : MoveFieldCommand {
    public AddFieldCommand(GameSession session, MapMetadataStorage mapStorage) : base(session, mapStorage, "addfield", "Move to a field (the client's GM map window).") { }
}

// "/ClearInven" from the GM item window: empties every inventory tab. Worn items stay.
public class ClearInvenCommand : GameCommand {
    private readonly GameSession session;

    public ClearInvenCommand(GameSession session) : base(AdminPermissions.GameMaster, "clearinven", "Empty the inventory (the client's GM item window).") {
        this.session = session;
        this.SetHandler<InvocationContext>(Handle);
    }

    private void Handle(InvocationContext ctx) {
        int count = 0;
        lock (session.Item) {
            foreach (Item item in session.Item.Inventory.Filter(_ => true).ToArray()) {
                if (session.Item.Inventory.Remove(item.Uid, out Item? removed)) {
                    session.Item.Inventory.Discard(removed);
                    count++;
                }
            }
        }
        ctx.Console.Out.WriteLine($"Inventory cleared: {count} items.");
        ctx.ExitCode = 0;
    }
}

// "/ToggleInvisibleFromNpc" (GM hotkey): monsters do not pick this player as a target.
public class ToggleInvisibleFromNpcCommand : GameCommand {
    private readonly GameSession session;

    public ToggleInvisibleFromNpcCommand(GameSession session) : base(AdminPermissions.GameMaster, "toggleinvisiblefromnpc", "Monsters ignore you (GM hotkey).") {
        this.session = session;
        this.SetHandler<InvocationContext>(Handle);
    }

    private void Handle(InvocationContext ctx) {
        session.Player.InvisibleFromNpc = !session.Player.InvisibleFromNpc;
        ctx.Console.Out.WriteLine($"InvisibleFromNpc: {(session.Player.InvisibleFromNpc ? "On" : "Off")}");
        ctx.ExitCode = 0;
    }
}

// "/ToggleDeathPower" (GM hotkey): every hit of this player kills the monster it lands on.
public class ToggleDeathPowerCommand : GameCommand {
    private readonly GameSession session;

    public ToggleDeathPowerCommand(GameSession session) : base(AdminPermissions.GameMaster, "toggledeathpower", "Your hits kill monsters (GM hotkey).") {
        this.session = session;
        this.SetHandler<InvocationContext>(Handle);
    }

    private void Handle(InvocationContext ctx) {
        session.Player.DeathPower = !session.Player.DeathPower;
        ctx.Console.Out.WriteLine($"DeathPower: {(session.Player.DeathPower ? "On" : "Off")}");
        ctx.ExitCode = 0;
    }
}
