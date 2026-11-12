using Maple2.Database.Storage;
using Maple2.Server.Game.Manager.Field;

namespace Maple2.Server.Game.Util;

// The maps whose fields are made only for a PvP match: the rank duel arenas and the individual arenas' fields
// (individualarena.xml fieldID). A plain move or a login there goes to the return field instead, and a room of them
// that is gone is not made again.
public static class PvpMatchMaps {
    public static IReadOnlyCollection<int> All(TableMetadataStorage? tableMetadata) {
        var maps = new HashSet<int>(RankDuelField.Maps);
        try {
            if (tableMetadata != null) {
                maps.UnionWith(tableMetadata.IndividualArenaTable.Entries.Values.Select(arena => arena.FieldId));
            }
        } catch (Exception) {
            // No arena table (a metadata database from before it): the rank duel maps only.
        }
        return maps;
    }

    // The arenas whose field runs a round the server builds: the map's script sets a PvP zone of the free-for-all
    // type (2), the Crimson Tear Mine 65000002. The Red Arena (type 1) and Treasure Island (type 3) zones are not
    // built, so their arenas are refused.
    public static readonly IReadOnlySet<int> PlayableArenaFields = new HashSet<int> { 65000002 };
}
