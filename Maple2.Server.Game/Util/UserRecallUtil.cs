using Grpc.Core;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.Util;

// The recall invite (RecallUser 0x8C) on one channel and across channels.
public static class UserRecallUtil {
    private static readonly object InviteLock = new();

    // Whether a player who accepted may be moved now: not while dead and not out of a dungeon room.
    public static bool CanBeMoved(bool dead, bool inDungeon) {
        return !dead && !inDungeon;
    }

    // Shows the invite to `target` (a session of this channel). Returns 0, or the failure value for the caller.
    public static int Invite(GameSession target, long callerId, string callerName, int mapId) {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        PendingUserRecall recall;
        // Two callers at once: the test and the set are one step, so only one invite goes out.
        lock (InviteLock) {
            if (!PendingUserRecall.CanInvite(target.UserRecall, now)) {
                // The client would answer a second invite with "busy" (c2s 7, byte 4) and show no box.
                return RecallUserPacket.FAIL_BUSY;
            }

            recall = new PendingUserRecall(PendingUserRecall.NextInviteId(), callerId, callerName, mapId, now + PendingUserRecall.VALID_SECONDS);
            target.UserRecall = recall;
        }
        target.Send(RecallUserPacket.Invite(recall.CallerName, recall.InviteId));
        return 0;
    }

    // Sends the invite through the World server to a player on another channel. Returns as Invite does.
    public static int InviteByWorld(GameSession caller, long targetId, int mapId) {
        try {
            PlayerWarpResponse response = caller.World.PlayerWarp(new PlayerWarpRequest {
                RequesterId = caller.CharacterId,
                RecallInvite = new PlayerWarpRequest.Types.RecallInvite {
                    TargetId = targetId,
                    CallerName = caller.PlayerName,
                    MapId = mapId,
                },
            });
            return response.Error is 0 or RecallUserPacket.FAIL_BUSY ? response.Error : RecallUserPacket.FAIL_NOT_FOUND;
        } catch (RpcException) {
            return RecallUserPacket.FAIL_NOT_FOUND;
        }
    }

    // The invited player's session ends (log off or a channel change) before the answer: the caller is told
    // "not found" instead of hearing nothing.
    public static void DropPending(GameSession target) {
        PendingUserRecall? pending = target.UserRecall;
        target.UserRecall = null;
        if (PendingUserRecall.IsOpen(pending, DateTimeOffset.UtcNow.ToUnixTimeSeconds())) {
            TellCaller(target, target.PlayerName, pending!.CallerId, false, RecallUserPacket.FAIL_NOT_FOUND);
        }
    }

    // Tells the caller how the invite ended: directly on this channel, else through the World server.
    // The name is passed in: after a channel change has started, the session may have no player any more.
    public static void TellCaller(GameSession target, string targetName, long callerId, bool recalled, byte fail) {
        if (target.FindSession(callerId, out GameSession? caller)) {
            caller.Send(RecallUserPacket.Result(targetName, recalled, fail));
            return;
        }

        try {
            target.World.PlayerWarp(new PlayerWarpRequest {
                RequesterId = target.CharacterId,
                RecallResult = new PlayerWarpRequest.Types.RecallResult {
                    CallerId = callerId,
                    TargetName = targetName,
                    Recalled = recalled,
                    Fail = fail,
                },
            });
        } catch (RpcException) {
            // The caller is gone; nobody to tell.
        }
    }
}
