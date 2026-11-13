using Maple2.Database.Storage;
using Maple2.Model.Metadata;

namespace Maple2.Server.Game.Util;

// The maps whose fields are made only for a dungeon room: each room's fields and its lobby (dungeonroom.xml
// fieldIDs and lobbyFieldID). A login on one whose room does not run goes to the return field.
public static class DungeonRoomMaps {
    public static IReadOnlySet<int> All(TableMetadataStorage? tableMetadata) {
        var maps = new HashSet<int>();
        if (tableMetadata == null) {
            return maps;
        }
        foreach (DungeonRoomMetadata room in tableMetadata.DungeonRoomTable.Entries.Values) {
            maps.UnionWith(room.FieldIds);
            if (room.LobbyFieldId != 0) {
                maps.Add(room.LobbyFieldId);
            }
        }
        return maps;
    }
}
