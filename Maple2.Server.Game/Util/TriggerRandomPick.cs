namespace Maple2.Server.Game.Util;

/// <summary>
/// The spawn ids a spawn_npc_range action spawns. random_pick_count N spawns N different ids of the range at random;
/// without it (0) the whole range spawns. Every one of the 61 placed actions that gives it picks fewer than its range
/// holds (3 or 4 of 64 on 02000385 and 02000393, 3 of 9 on 02000392, 02000432 and 52100002, 1 of 4 and 1 of 3 on
/// 52010038, 3 of 20 on 99999908), and every id of those ranges is a placed one-npc spawn point.
/// </summary>
public static class TriggerRandomPick {
    public static int[] Pick(int[] ids, int randomPickCount, Random random) {
        if (randomPickCount <= 0 || randomPickCount >= ids.Length) {
            return ids;
        }

        int[] shuffled = ids.ToArray();
        random.Shuffle(shuffled);
        return shuffled[..randomPickCount];
    }
}
