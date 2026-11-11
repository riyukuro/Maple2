using Grpc.Core;
using Maple2.Model.Game;

namespace Maple2.Server.World.Service;

public partial class WorldService {
    // The profile of a player who is online on another channel: that channel writes the CharacterInfo packet. An
    // empty packet when the player is offline or the channel does not answer.
    public override Task<CharacterInfoResponse> CharacterInfo(CharacterInfoRequest request, ServerCallContext context) {
        if (!playerLookup.TryGet(request.CharacterId, out PlayerInfo? info) || !info.Online
            || !channelClients.TryGetClient(info.Channel, out Channel.Service.Channel.ChannelClient? channelClient)) {
            return Task.FromResult(new CharacterInfoResponse());
        }

        try {
            return Task.FromResult(channelClient.CharacterInfo(request));
        } catch (RpcException) {
            return Task.FromResult(new CharacterInfoResponse());
        }
    }
}
