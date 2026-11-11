using Grpc.Core;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Game.Service;

public partial class ChannelService {
    public override Task<PlayerWarpResponse> PlayerWarp(PlayerWarpRequest request, ServerCallContext context) {
        switch (request.RequestCase) {
            case PlayerWarpRequest.RequestOneofCase.GoToPlayer:
                return Task.FromResult(GoToPlayer(request.GoToPlayer));
            case PlayerWarpRequest.RequestOneofCase.RecallInvite:
                return Task.FromResult(RecallInvite(request.RequesterId, request.RecallInvite));
            case PlayerWarpRequest.RequestOneofCase.RecallResult:
                return Task.FromResult(RecallResult(request.RecallResult));
            default:
                return Task.FromResult(new PlayerWarpResponse());
        }
    }

    // A GM on another channel invites a player of this channel. The GM check was done where the command ran.
    private PlayerWarpResponse RecallInvite(long callerId, PlayerWarpRequest.Types.RecallInvite invite) {
        if (callerId == 0 || !server.GetSession(invite.TargetId, out GameSession? target)) {
            return new PlayerWarpResponse {
                Error = RecallUserPacket.FAIL_NOT_FOUND,
            };
        }

        return new PlayerWarpResponse {
            Error = UserRecallUtil.Invite(target, callerId, invite.CallerName, invite.MapId),
        };
    }

    // The result of an invite a GM of this channel sent to a player on another channel.
    private PlayerWarpResponse RecallResult(PlayerWarpRequest.Types.RecallResult result) {
        if (server.GetSession(result.CallerId, out GameSession? caller)) {
            caller.Send(RecallUserPacket.Result(result.TargetName, result.Recalled, (byte) result.Fail));
        }
        return new PlayerWarpResponse();
    }

    private PlayerWarpResponse GoToPlayer(PlayerWarpRequest.Types.GoToPlayer gotoPlayer) {

        if (!server.GetSession(gotoPlayer.CharacterId, out GameSession? session)) {
            return new PlayerWarpResponse {
                Error = -1,
            };
        }

        if (session.Field is null)
            return new PlayerWarpResponse() {
                Error = -1,
            };

        return new PlayerWarpResponse {
            RoomId = session.Field.RoomId,
            X = session.Player.Position.X,
            Y = session.Player.Position.Y,
            Z = session.Player.Position.Z,
        };
    }
}

