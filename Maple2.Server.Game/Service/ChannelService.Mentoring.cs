using Grpc.Core;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.Service;

public partial class ChannelService {
    public override Task<MentoringResponse> Mentoring(MentoringRequest request, ServerCallContext context) {
        if (!server.GetSession(request.ReceiverId, out GameSession? session)) {
            throw new RpcException(new Status(StatusCode.NotFound, $"Unable to find: {request.ReceiverId}"));
        }

        switch (request.MentoringCase) {
            case MentoringRequest.MentoringOneofCase.Requested:
                session.Mentoring.ReceiveRequest(request.Requested.RequestId);
                break;
            case MentoringRequest.MentoringOneofCase.Reload:
                session.Mentoring.Reload();
                break;
        }

        return Task.FromResult(new MentoringResponse());
    }
}
