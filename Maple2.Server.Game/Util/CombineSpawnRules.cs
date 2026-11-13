using Maple2.Model.Enum;
using Maple2.Model.Metadata;

namespace Maple2.Server.Game.Util;

// Which combineSpawnGroup row a field's start_combine_spawn takes.
public static class CombineSpawnRules {
    public enum Source { Own, OtherMap, Missing, NotOnField }

    // The table lists each group under one fieldId, but some maps are copies that run the same scripts on the same
    // entities: 82000000 runs 82000001's groups and 82000006 runs 82000012's, and their own fieldId has no group.
    // Group ids are unique across the table, so a group the field's own map does not list is taken from the map
    // that does. Chosen here: only when every spawn point (npc groups) or region spawn (interact object groups) it
    // names is on this field; any other group is skipped.
    public static Source Find(CombineSpawnTable table, int mapId, int groupId, Func<int, bool> hasNpcSpawn, Func<int, bool> hasRegionSpawn, out SpawnGroupMetadata? group) {
        if (table.Groups.TryGetValue(mapId, out Dictionary<int, SpawnGroupMetadata>? own) && own.TryGetValue(groupId, out group)) {
            return Source.Own;
        }

        group = null;
        foreach (Dictionary<int, SpawnGroupMetadata> groups in table.Groups.Values) {
            if (groups.TryGetValue(groupId, out group)) {
                break;
            }
        }
        if (group == null) {
            return Source.Missing;
        }

        bool onField = group.Type switch {
            CombineSpawnGroupType.npc => table.Npcs.TryGetValue(groupId, out Dictionary<int, SpawnNpcMetadata>? npcs) && npcs.Count > 0 && npcs.Values.All(npc => hasNpcSpawn(npc.SpawnId)),
            CombineSpawnGroupType.interactObject => table.InteractObjects.TryGetValue(groupId, out Dictionary<int, SpawnInteractObjectMetadata>? objects) && objects.Count > 0 && objects.Values.All(obj => hasRegionSpawn(obj.RegionSpawnId)),
            _ => false,
        };
        if (!onField) {
            group = null;
            return Source.NotOnField;
        }
        return Source.OtherMap;
    }
}

// How many spawns a combine spawn group keeps up.
public static class SpawnGroupRules {
    // The group's totalCount, capped at the number of distinct spawn ids it can pick: a pick that is already up is
    // drawn again, so a cap above that (or any count for an empty group) would never be reached. Spawns still up
    // after a reset count toward it, so a reset only fills the gap.
    public static int TotalCount(int totalCount, IEnumerable<int> spawnIds) {
        return Math.Clamp(totalCount, 0, spawnIds.Distinct().Count());
    }
}
