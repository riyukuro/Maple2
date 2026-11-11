using Maple2.Model.Game;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.Util;

// The architect scores of a home (its commendations this month and of all time) live in two copies on a channel:
// the loaded home field's Home, shown to its visitors (ResponseCube 0x1c), and the owner's own Player.Home, shown
// with the owner's PC in every field it enters (FieldPacket.AddPlayer) and in the owner's MyInfo dialog. The client
// takes a change of the PC's copy from HomeCommand 1 (CSceneStateGame::OnHomeCommand): it finds the PC by object id
// in its own field and, for its own PC, refreshes the MyInfo dialog; so the owner's field gets it, wherever it is.
public static class HomeArchitectScore {
    // Sets both copies on this channel and tells the fields that show a copy that changed. Either may be absent.
    public static void Apply(HomeFieldManager? home, GameSession? owner, int current, int total) {
        if (home != null && Set(home.Home, current, total)) {
            home.Broadcast(CubePacket.ArchitectScore(current, total));
        }
        // Player is set once the session has entered the game.
        if (owner?.Player is { } player && Set(player.Value.Home, current, total)) {
            owner.Field?.Broadcast(HomeCommandPacket.UpdateArchitectScore(player.ObjectId, current, total));
        }
    }

    // Sets the scores; false when they were already these, so nothing needs to be sent.
    public static bool Set(Home home, int current, int total) {
        if (home.CurrentArchitectScore == current && home.ArchitectScore == total) {
            return false;
        }
        home.CurrentArchitectScore = current;
        home.ArchitectScore = total;
        return true;
    }

    // The request that carries a commendation's scores to the other channels.
    public static FieldPlotRequest Request(int homeMapId, long ownerId, int current, int total, int ignoreChannel) {
        return new FieldPlotRequest {
            IgnoreChannel = ignoreChannel,
            MapId = homeMapId,
            ArchitectScore = new FieldPlotRequest.Types.ArchitectScore {
                OwnerId = ownerId,
                Current = current,
                Total = total,
            },
        };
    }
}
