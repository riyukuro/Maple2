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

    // The spots of a region spawn that has no spawn tile in reach: its point stands on a mesh's floor (a road, a
    // bridge, a roof), and spawn tiles are only made over ground cubes. The spots are the point and its 8 block
    // neighbours within range, each where ground(candidate) finds walkable ground (the navmesh) under it: no further
    // than half a block aside and one block up or down, so a neighbour past a ledge or inside a wall is left out.
    public static List<Vector3> GroundSpots(Vector3 point, float block, float range, Func<Vector3, Vector3?> ground) {
        var spots = new List<Vector3>();
        for (int dx = -1; dx <= 1; dx++) {
            for (int dy = -1; dy <= 1; dy++) {
                Vector3 candidate = point + new Vector3(dx * block, dy * block, 0);
                if (Vector3.Distance(candidate, point) > range) {
                    continue;
                }
                if (ground(candidate) is not { } spot) {
                    continue;
                }
                if (MathF.Abs(spot.X - candidate.X) > block / 2 || MathF.Abs(spot.Y - candidate.Y) > block / 2 || MathF.Abs(spot.Z - point.Z) > block) {
                    continue;
                }
                spots.Add(spot);
            }
        }
        return spots;
    }
}
