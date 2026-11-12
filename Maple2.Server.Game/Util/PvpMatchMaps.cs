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

    // The arenas whose field runs a round the server builds: their map scripts set PvP zones of type 1 (Red Arena
    // 65000001), 2 (Crimson Tear Mine 65000002) and 3 (Treasure Island 65000003).
    public static readonly IReadOnlySet<int> PlayableArenaFields = new HashSet<int> { 65000001, 65000002, 65000003 };
}
