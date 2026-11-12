using System.Collections.Concurrent;
using Maple2.Database.Storage;
using Maple2.Model.Validators;

namespace Maple2.Server.Game.Util;

// The ground taxi's hop count as the client finds it for the fare it shows (CTaxiManager::GetTaxiFare): the fewest
// portal hops over the fields' portal targets of fieldmetadata.xml. A field counts as enabled (IsFieldEnabled) when
// the server has its map.
public sealed class TaxiRouteStorage {
    private readonly TableMetadataStorage tableMetadata;
    private readonly MapMetadataStorage mapMetadata;
    private readonly ConcurrentDictionary<int, bool> enabled = new();

    public TaxiRouteStorage(TableMetadataStorage TableMetadata, MapMetadataStorage MapMetadata) {
        tableMetadata = TableMetadata;
        mapMetadata = MapMetadata;
    }

    // The portal hops from mapOrigin to mapDestination, 0 for the same map, -1 when no portals lead there.
    public int Hops(int mapOrigin, int mapDestination) {
        return TaxiRules.PortalHops(mapOrigin, mapDestination, tableMetadata.FieldMetaTable.Portals, IsEnabled);
    }

    private bool IsEnabled(int mapId) {
        return enabled.GetOrAdd(mapId, id => mapMetadata.TryGet(id, out _));
    }
}
