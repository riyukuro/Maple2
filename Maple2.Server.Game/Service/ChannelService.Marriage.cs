using Grpc.Core;
using Maple2.Model.Enum;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.Service;

public partial class ChannelService {
    public override Task<MarriageResponse> Marriage(MarriageRequest request, ServerCallContext context) {
        if (!server.GetSession(request.ReceiverId, out GameSession? session)) {
            throw new RpcException(new Status(StatusCode.NotFound, $"Unable to find: {request.ReceiverId}"));
        }

        switch (request.MarriageCase) {
            case MarriageRequest.MarriageOneofCase.RemoveMarriage:
                session.Marriage.RemoveMarriage();
                break;
            case MarriageRequest.MarriageOneofCase.RemoveWeddingHall:
                session.Marriage.RemoveWeddingHall();
                break;
            case MarriageRequest.MarriageOneofCase.Reload:
                session.Marriage.Reload();
                break;
            case MarriageRequest.MarriageOneofCase.Recall:
                return Task.FromResult(Recall(session, request.Recall));
        }

        return Task.FromResult(new MarriageResponse());
    }

    // The partner's wedding recall: stored for the answer and shown as RecallScroll type 2.
    private static MarriageResponse Recall(GameSession session, MarriageRequest.Types.Recall recall) {
        Maple2.Model.Game.Marriage marriage = session.Marriage.Marriage;
        if (!MarriageManager.CanRecall(marriage.Status) || session.Marriage.Partner.CharacterId != recall.CallerId) {
            return new MarriageResponse { Error = 1 };
        }

        session.OfferRecall(RecallScrollType.Wedding, new PendingRecall(recall.CallerId, recall.Name, recall.MapId, recall.EndTime));
        return new MarriageResponse();
    }
}
