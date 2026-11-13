using Grpc.Core;
using Maple2.Model.Game;
using ChannelClient = Maple2.Server.Channel.Service.Channel.ChannelClient;

namespace Maple2.Server.World.Service;

public partial class WorldService {
    // Hands a mentoring change to the receiver's game server; an offline receiver reads its rows at its next login.
    public override Task<MentoringResponse> Mentoring(MentoringRequest request, ServerCallContext context) {
        if (!playerLookup.TryGet(request.ReceiverId, out PlayerInfo? info) || !WorldForwardRules.Forward(info)) {
            return Task.FromResult(new MentoringResponse());
        }

        int channel = info.Channel;
        if (!channelClients.TryGetClient(channel, out ChannelClient? channelClient)) {
            logger.Error("No registry for channel: {Channel}", channel);
            return Task.FromResult(new MentoringResponse());
        }

        try {
            return Task.FromResult(channelClient.Mentoring(request));
        } catch (RpcException ex) when (ex.StatusCode is StatusCode.NotFound) {
            logger.Information("{ReceiverId} not found...", request.ReceiverId);
            return Task.FromResult(new MentoringResponse());
        }
    }
}
