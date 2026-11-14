using Maple2.Model.Metadata;

namespace Maple2.Server.Game.Util;

// How many npcs a plain SpawnPointNPC keeps up, and which. The point's NpcCount is the total; each NpcList entry's
// Count stays a cap on that npc. 02020051 has 33 points listing 23001008/09/10 at 5/3/2 with NpcCount 5: five of the
// ten listed npcs are up per point, not all ten. A count above the list total (21000059 x1 with NpcCount 3) spawns the
// list, as before; a row ingested before the field (NpcCount 0) does too.
public static class SpawnPointRules {
    public static int PlainCap(SpawnPointNPC point) {
        int total = point.NpcList.Sum(entry => Math.Max(entry.Count, 0));
        return point.NpcCount > 0 ? Math.Min(point.NpcCount, total) : total;
    }

    // The npc ids to spawn so the point is back to its cap, given the ids of the npcs alive under its spawn id. Only
    // ids on the point's list fill its slots: other actors can share the spawn id (summons and pets at 0, QuestMask
    // partner points at 0, plain points or trigger spawns on the same id) and must not hold the point's slots. Only
    // npcs the field can spawn (known) go in the pool; the cap itself counts the whole list. When fewer slots than
    // candidates are open, a random pick of them.
    public static List<int> PickNpcs(SpawnPointNPC point, IReadOnlyCollection<int> aliveIds, Func<int, bool> known, Random random) {
        HashSet<int> listed = point.NpcList.Select(entry => entry.NpcId).ToHashSet();
        int open = PlainCap(point) - aliveIds.Count(listed.Contains);
        if (open <= 0) {
            return [];
        }

        List<int> pool = [];
        foreach (SpawnPointNPCListEntry entry in point.NpcList) {
            if (!known(entry.NpcId)) {
                continue;
            }
            int missing = entry.Count - aliveIds.Count(id => id == entry.NpcId);
            for (int i = 0; i < missing; i++) {
                pool.Add(entry.NpcId);
            }
        }

        return pool.Count <= open ? pool : pool.OrderBy(_ => random.Next()).Take(open).ToList();
    }
}
