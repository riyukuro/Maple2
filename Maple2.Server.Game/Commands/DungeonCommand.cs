using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.IO;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.Commands;

// Makes a dungeon room by its room id and enters it through the same path as the client's create request
// (RoomDungeon 2, DungeonManager.CreateDungeonRoom): the room's enter limits and user counts are checked, the
// world makes the room, and the player (with --party, the party leader's party) moves into it.
public class DungeonCommand : GameCommand {
    private readonly GameSession session;

    public DungeonCommand(GameSession session) : base(AdminPermissions.Warp, "dungeon", "Make and enter a dungeon room by room id.") {
        this.session = session;

        var dungeonId = new Argument<int>("id", "Id of the dungeon room (Table/DungeonRoom).");
        var party = new Option<bool>(["--party", "-p"], "Make the room for your party (you must lead it).");

        AddArgument(dungeonId);
        AddOption(party);
        this.SetHandler<InvocationContext, int, bool>(Handle, dungeonId, party);
    }

    private void Handle(InvocationContext ctx, int dungeonId, bool party) {
        if (!session.TableMetadata.DungeonRoomTable.Entries.TryGetValue(dungeonId, out DungeonRoomMetadata? _)) {
            ctx.Console.Error.WriteLine($"Invalid dungeon room: {dungeonId}");
            ctx.ExitCode = 1;
            return;
        }

        ctx.Console.Out.WriteLine($"Making dungeon room {dungeonId}");
        session.Dungeon.CreateDungeonRoom(dungeonId, party);
        ctx.ExitCode = 0;
    }
}
