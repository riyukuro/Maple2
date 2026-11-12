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
                    RankDuelError error = pvpLookup.Join(request.RequesterId, out int channel, out int mapId, out int roomId, out bool observer);
                    return Task.FromResult(new PvpResponse { Error = (int) error, Channel = channel, MapId = mapId, RoomId = roomId, Observer = observer });
                }
            case PvpRequest.PvpOneofCase.Observable:
                pvpLookup.Observable(request.Observable.MatchId);
                return Task.FromResult(new PvpResponse());
            case PvpRequest.PvpOneofCase.End:
                if (request.End.Arena != 0) {
                    pvpLookup.ArenaEnd(request.End.MatchId);
                } else {
                    pvpLookup.End(request.End.MatchId);
                }
                return Task.FromResult(new PvpResponse());
            case PvpRequest.PvpOneofCase.ArenaRegister: {
                var response = new PvpResponse {
                    Error = (int) pvpLookup.ArenaRegister(request.RequesterId, request.ArenaRegister.Arena, request.ArenaRegister.PlayerCount),
                };
                response.ArenaWaits.Add(pvpLookup.ArenaWaits());
                return Task.FromResult(response);
            }
            case PvpRequest.PvpOneofCase.ArenaUnregister: {
                var response = new PvpResponse { Error = (int) pvpLookup.ArenaUnregister(request.RequesterId) };
                response.ArenaWaits.Add(pvpLookup.ArenaWaits());
                return Task.FromResult(response);
            }
            case PvpRequest.PvpOneofCase.ArenaJoin: {
                RankDuelError error = pvpLookup.ArenaJoin(request.RequesterId, out int channel, out int mapId, out int roomId);
                return Task.FromResult(new PvpResponse { Error = (int) error, Channel = channel, MapId = mapId, RoomId = roomId });
            }
            default:
                return Task.FromResult(new PvpResponse());
        }
    }
}
