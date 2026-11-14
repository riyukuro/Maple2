using Grpc.Core;
using Maple2.Model.Validators;

namespace Maple2.Server.World.Service;

public partial class WorldService {
    public override Task<MapPopulationResponse> MapPopulation(MapPopulationRequest request, ServerCallContext context) {
        var response = new MapPopulationResponse();
        foreach ((int mapId, short channel, int count) in WorldMapPopulationRules.Count(playerLookup.GetOnlinePlayerInfos()
                     .Select(player => (player.MapId, player.Channel, player.DungeonId)))) {
            response.Entries.Add(new MapPopulationResponse.Types.Entry { MapId = mapId, Channel = channel, Count = count });
        }
        return Task.FromResult(response);
    }
}
