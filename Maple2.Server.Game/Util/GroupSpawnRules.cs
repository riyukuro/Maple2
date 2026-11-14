using Maple2.Model.Metadata;

namespace Maple2.Server.Game.Util;

// An EventSpawnGroupNPC point (Dark Stream) spawns one of its npc groups: the group is picked by weight, then Count
// npcs are spawned, each a random npc of that group. On 03100001 a point lists groups 1/2/3 at counts 12/6/3 and
// weights 1/1/1, and groupSpawn.xml scores those groups' npcs 3/6/12: every pick is worth the same 36.
public static class GroupSpawnRules {
    // With no weight on any group (a point whose weight is listed under another index), every group weighs 1.
    public static int TotalWeight(IList<SpawnGroupEntry> groups) {
        int total = groups.Sum(group => group.Weight);
        return total > 0 ? total : groups.Count;
    }

    // The group a roll in [0, TotalWeight) lands on.
    public static SpawnGroupEntry? Pick(IList<SpawnGroupEntry> groups, int roll) {
        bool weighted = groups.Any(group => group.Weight > 0);
        foreach (SpawnGroupEntry group in groups) {
            int weight = weighted ? group.Weight : 1;
            if (roll < weight) {
                return group;
            }
            roll -= weight;
        }
        return null;
    }

    public static List<int> NpcIds(EventSpawnGroupNPC point, Random random) {
        if (point.Groups.Count == 0 || Pick(point.Groups, random.Next(TotalWeight(point.Groups))) is not { } group) {
            return [];
        }
        return Enumerable.Range(0, group.Count).Select(_ => group.NpcIds[random.Next(group.NpcIds.Length)]).ToList();
    }
}
