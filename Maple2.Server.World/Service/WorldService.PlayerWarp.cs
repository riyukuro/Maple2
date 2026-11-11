using Grpc.Core;
using Maple2.Model.Game;

namespace Maple2.Server.World.Service;

public partial class WorldService {
    public override Task<PlayerWarpResponse> PlayerWarp(PlayerWarpRequest request, ServerCallContext context) {
        switch (request.RequestCase) {
            case PlayerWarpRequest.RequestOneofCase.GoToPlayer:
                return Task.FromResult(GoToPlayer(request, request.RequesterId));
            case PlayerWarpRequest.RequestOneofCase.RecallInvite:
                return Task.FromResult(RecallRelay(request, request.RecallInvite.TargetId));
            case PlayerWarpRequest.RequestOneofCase.RecallResult:
                return Task.FromResult(RecallRelay(request, request.RecallResult.CallerId));
            default:
                return Task.FromResult(new PlayerWarpResponse());
        }
    }

    // Passes a recall invite or its result to the channel the receiver is on. Error 6 is the client's
    // "not found" value of RecallUser kind 7.
    private PlayerWarpResponse RecallRelay(PlayerWarpRequest request, long receiverId) {
        if (receiverId == 0 || !playerLookup.TryGet(receiverId, out PlayerInfo? info) || !info.Online
            || !channelClients.TryGetClient(info.Channel, out Channel.Service.Channel.ChannelClient? channelClient)) {
            return new PlayerWarpResponse {
                Error = RECALL_NOT_FOUND,
            };
        }

        try {
            return channelClient.PlayerWarp(request);
        } catch (RpcException) {
            return new PlayerWarpResponse {
                Error = RECALL_NOT_FOUND,
            };
        }
    }

    private const int RECALL_NOT_FOUND = 6;

    private PlayerWarpResponse GoToPlayer(PlayerWarpRequest request, long requesterId) {
        if (request.GoToPlayer.CharacterId == 0) {
            return new PlayerWarpResponse {
                Error = -1,
            };
        }

        if (channelClients.TryGetClient(request.GoToPlayer.Channel, out Channel.Service.Channel.ChannelClient? channelClient)) {
            return channelClient.PlayerWarp(request);
        }

        return new PlayerWarpResponse {
            Error = -1,
        };
    }
}
