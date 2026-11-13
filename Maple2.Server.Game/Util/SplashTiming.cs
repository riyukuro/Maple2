using System.Numerics;

namespace Maple2.Server.Game.Util;

// When a splash sub-skill's region fires for the first time, in ms after the region is placed.
public static class SplashTiming {
    // immediateActive: at once, Delay is not used.
    // otherwise: Delay + Interval; the later fires follow every Interval.
    // onlySensingActive: the region starts looking for a target one Interval after it is placed and Delay only
    // bounds how long it looks (chosen here).
    public static int FirstFire(bool immediateActive, bool onlySensingActive, int delay, int interval) {
        if (immediateActive) {
            return 0;
        }
        if (onlySensingActive) {
            return interval;
        }
        return delay + interval;
    }

    // Whether a region that fires only when made to (onlyManualActiveSplash) fires now: within its splashLifeTick and
    // splashInvokeCoolTick after its last fire. Chosen here: an invoke at the very tick the cool time or the life
    // ends still fires.
    public static bool CanInvoke(long now, long endTick, long nextInvokeTick) => now <= endTick && now >= nextInvokeTick;

    // What a fire of a region sends. The client plays the region's hit effect for every region damage packet,
    // before it reads the targets (CSceneStateGame::OnRegionSkillDamage), and KMS2 sends one for each fire: four
    // in five name no target, and no target packet goes before them. A cube skill's fire (the tile packet,
    // one for each target) is sent only when it reached someone, and KMS2 sends no target packet before it either.
    public static (bool Targets, bool Damage) FirePackets(bool cube, int targetCount) {
        return cube ? (false, targetCount > 0) : (false, true);
    }

    // The position a region damage packet gives with a target: KMS2 sends one of the region's own points there
    // (the points of its RegionSkill add packet), not the caster's position. Chosen here for a region of several
    // points: the one nearest the target. A region without points gives its own position.
    public static Vector3 FirePoint(IReadOnlyList<Vector3> points, Vector3 target, Vector3 region) {
        Vector3 best = region;
        float bestDistance = float.MaxValue;
        foreach (Vector3 point in points) {
            float distance = Vector3.DistanceSquared(point, target);
            if (distance < bestDistance) {
                best = point;
                bestDistance = distance;
            }
        }
        return best;
    }

    // The direction a region damage packet gives with a target: in KMS2 packets the unit vector from that point
    // to the target, in all three axes, and zero when the target stands on the point. A tile packet's direction
    // follows the same rule from its own point (TilePoint) in KMS2 packets.
    public static Vector3 FireDirection(Vector3 point, Vector3 target) {
        Vector3 offset = target - point;
        return offset == Vector3.Zero ? Vector3.Zero : Vector3.Normalize(offset);
    }

    // The point a tile packet gives for a cube skill: in KMS2 packets one block below the map's position of the
    // cube that holds the skill.
    public static Vector3 TilePoint(Vector3 cube) {
        return cube with { Z = cube.Z - BLOCK };
    }

    private const float BLOCK = 150f;
}
