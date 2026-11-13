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

    // The spawn points the field's npc groups name: those of its own map's npc groups or, on a map the table lists
    // no group for (a copy such as 82000000), those of the other maps' npc groups its own triggers start and Find
    // takes here (all of its points on the field). Such a point spawns only when its group picks it
    // (FieldSpawnPointNpc). Chosen here: a map with groups of its own takes no other map's, as 02020062 has the point
    // ids 611-614 of 02020061's group 483 but runs only its own groups 490-495; and a map without takes only the
    // groups its triggers start, as group point ids are small and reused (02000402 has the points 501-504 of the
    // groups 518 and 524 of 02020300 and 52100300 but starts no group, so they spawn as plain points).
    public static HashSet<int> GroupSpawnIds(CombineSpawnTable table, int mapId, Func<int, bool> hasNpcSpawn, IEnumerable<int> startedGroups) {
        var spawnIds = new HashSet<int>();
        IEnumerable<int> groupIds = table.Groups.TryGetValue(mapId, out Dictionary<int, SpawnGroupMetadata>? own)
            ? own.Keys
            : startedGroups.Distinct();
        foreach (int groupId in groupIds) {
            if (Find(table, mapId, groupId, hasNpcSpawn, _ => false, out SpawnGroupMetadata? group) is not (Source.Own or Source.OtherMap)
                || group!.Type != CombineSpawnGroupType.npc || !table.Npcs.TryGetValue(groupId, out Dictionary<int, SpawnNpcMetadata>? npcs)) {
                continue;
            }
            foreach (SpawnNpcMetadata npc in npcs.Values) {
                spawnIds.Add(npc.SpawnId);
            }
        }
        return spawnIds;
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

    // A group's fill (start_combine_spawn, then each reset): the picks no longer up (killed, or a box opened) are
    // dropped, then new picks (pick, drawn by weight; one already up is drawn again) are put up (spawn) until
    // totalCount are up. Returns the spawn ids put up.
    public static List<int> Refill(List<int> upSpawnIds, Func<int, bool> isUp, int totalCount, Func<int> pick, Action<int> spawn) {
        upSpawnIds.RemoveAll(spawnId => !isUp(spawnId));
        var added = new List<int>();
        while (upSpawnIds.Count < totalCount) {
            int spawnId = pick();
            if (upSpawnIds.Contains(spawnId)) {
                continue;
            }
            spawn(spawnId);
            upSpawnIds.Add(spawnId);
            added.Add(spawnId);
        }
        return added;
    }

    // A spawn point fills itself (on its regen check) and at field creation only when no group of the field names it.
    public static bool SelfFills(SpawnPointNPC point, bool groupSpawned) => point.RegenCheckTime > 0 && !groupSpawned;
    public static bool SpawnsOnCreate(SpawnPointNPC point, bool groupSpawned) => point.SpawnOnFieldCreate && !groupSpawned;
}
