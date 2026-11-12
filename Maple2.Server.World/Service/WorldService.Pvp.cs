using Grpc.Core;
using Maple2.Model.Game.Pvp;

namespace Maple2.Server.World.Service;

public partial class WorldService {
    public override Task<PvpResponse> Pvp(PvpRequest request, ServerCallContext context) {
        switch (request.PvpCase) {
            case PvpRequest.PvpOneofCase.Register:
                return Task.FromResult(new PvpResponse { Error = (int) pvpLookup.Register(request.RequesterId, request.Register.Rating) });
            case PvpRequest.PvpOneofCase.Unregister:
                return Task.FromResult(new PvpResponse { Error = (int) pvpLookup.Unregister(request.RequesterId) });
            case PvpRequest.PvpOneofCase.Join: {
                    RankDuelError error = pvpLookup.Join(request.RequesterId, out int channel, out int mapId, out int roomId);
                    return Task.FromResult(new PvpResponse { Error = (int) error, Channel = channel, MapId = mapId, RoomId = roomId });
                }
            case PvpRequest.PvpOneofCase.End:
                pvpLookup.End(request.End.MatchId);
                return Task.FromResult(new PvpResponse());
            default:
                return Task.FromResult(new PvpResponse());
        }
    }
}
