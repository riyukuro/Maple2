using Maple2.Model.Enum;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Session;
using Serilog;

namespace Maple2.Server.Game.Util;

// The gate for packets and packet modes the client sends only from a GM command, a debug hotkey
// or a developer dialog. Every such handler or mode starts with
//     if (!GmCheck.Allow(session, OpCode, (int) command)) return;
// A sender that is not a GM is dropped: one warning, no reply.
public static class GmCheck {
    private static readonly ILogger Logger = Log.Logger.ForContext(typeof(GmCheck));

    // GameMaster or Admin (Admin holds every flag). A single finer flag, such as Warp, is not enough.
    public static bool IsGm(AdminPermissions permissions) {
        return permissions.HasFlag(AdminPermissions.GameMaster);
    }

    // A GM that also holds every flag of `also` (the flag of the matching chat command).
    public static bool IsGm(AdminPermissions permissions, AdminPermissions also) {
        return IsGm(permissions) && (permissions & also) == also;
    }

    public static bool Allow(GameSession session, RecvOp opCode, int mode = -1) {
        return Allow(session, opCode, AdminPermissions.None, mode);
    }

    // `also`: a finer flag the packet needs on top of the GM check (SpawnItem, SpawnNpc).
    public static bool Allow(GameSession session, RecvOp opCode, AdminPermissions also, int mode = -1) {
        if (session.Player != null && IsGm(session.Player.AdminPermissions, also)) {
            return true;
        }

        if (mode < 0) {
            Logger.Warning("GM-only packet {OpCode} from account {AccountId} dropped", opCode, session.AccountId);
        } else {
            Logger.Warning("GM-only packet {OpCode} mode {Mode} from account {AccountId} dropped", opCode, mode, session.AccountId);
        }
        return false;
    }
}
