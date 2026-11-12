using System.Numerics;

namespace Maple2.Server.Game.Util;

// The positions of a region spawn's mobs: each takes a spawn tile of its own (picked at random) while there are tiles
// left; past them a mob goes to spread(tile) of a random tile, a point around it, so a region with fewer tiles than
// mobs (a field floored by a mesh has few ground cubes) does not stack its mobs on one spot.
public static class SpawnPicker {
    public static List<Vector3> Pick(IReadOnlyList<Vector3> tiles, int count, Random random, Func<Vector3, Vector3> spread) {
        var picked = new List<Vector3>(Math.Max(count, 0));
        if (tiles.Count == 0 || count <= 0) {
            return picked;
        }

        Vector3[] remaining = tiles.ToArray();
        int distinct = Math.Min(count, remaining.Length);
        for (int i = 0; i < distinct; i++) {
            int left = remaining.Length - i;
            int index = random.Next(left);
            picked.Add(remaining[index]);
            remaining[index] = remaining[left - 1]; // the last unpicked tile takes the picked one's place
        }

        for (int i = distinct; i < count; i++) {
            picked.Add(spread(tiles[random.Next(tiles.Count)]));
        }
        return picked;
    }
}
