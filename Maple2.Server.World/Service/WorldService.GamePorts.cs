
using Grpc.Core;

namespace Maple2.Server.World.Service;

public partial class WorldService {
    public override Task<AddChannelResponse> AddChannel(AddChannelRequest request, ServerCallContext context) {
        if (request.HasChannel) {
            (ushort port, int grpc, int id, bool known) = channelClients.Register(request.GameIp, request.GrpcGameIp, request.InstancedContent, request.Channel, request.Instance);
            return Task.FromResult(new AddChannelResponse {
                GamePort = port,
                GrpcPort = grpc,
                GameChannel = id,
                Known = known,
            });
        }

        (ushort gamePort, int grpcPort, int channel) = channelClients.FindOrCreateChannelByIp(request.GameIp, request.GrpcGameIp, request.InstancedContent, request.Instance);

        return Task.FromResult(new AddChannelResponse {
            GamePort = gamePort,
            GrpcPort = grpcPort,
            GameChannel = channel,
        });
    }
}
