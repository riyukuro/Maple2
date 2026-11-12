using System.Numerics;

namespace Maple2.Server.Game.Util;

// Bounds on the attack packets of a player's cast. The client names the cast, the attack point and the positions;
// the server takes them only inside what KMS2 clients send. Every number here is chosen here.
public static class SkillHitRules {
    // A cast position further than this from the player's own position is not taken. KMS2 casts lie within 507 of
    // the position the client synced in the second before (median 0.9, 99 in 100 within 191).
    public const float CastReach = 600f;
    // The player's position counts as known for this long after a sync; a client that stands still sends none.
    public const long FreshSyncMs = 1000;
    // KMS2 attack point packets carry the cast's own position.
    public const float PointSlack = 1f;
    // KMS2 impact positions lie within 730 of the cast and within 3.0 times the attack's range distance
    // (99 in 100 within 450 and 2.3 times).
    public const float ImpactFactor = 3f;
    public const float ImpactExtra = 750f;
    // KMS2 attack point packets declare 0, 1, 3, 4, 10 or 20 attacks.
    public const int MaxPointAttacks = 20;
    // KMS2 sends the last attack packet of a cast at most 3.55 s after the cast (99 in 100 within 0.82 s).
    public const long HitWindowMs = 5000;

    public static bool CastTooFar(Vector3 cast, Vector3 player, long sinceSyncMs) {
        return sinceSyncMs <= FreshSyncMs && Vector3.Distance(cast, player) > CastReach;
    }

    public static bool PointMoved(Vector3 point, Vector3 cast) => Vector3.Distance(point, cast) > PointSlack;

    public static float ImpactReach(float rangeDistance) {
        float distance = MathF.Max(0f, rangeDistance);
        return MathF.Max(ImpactFactor * distance, distance + ImpactExtra);
    }

    public static bool ImpactTooFar(Vector3 impact, Vector3 cast, float rangeDistance) {
        return Vector3.Distance(impact, cast) > ImpactReach(rangeDistance);
    }

    public static bool TooLate(long tick, long castTick) => tick - castTick > HitWindowMs;
}

// What one cast may still hit. An attack point packet declares attacks by uid; a target packet pays damage for one
// declared attack. In KMS2 each attack point is sent once per cast, a skill that continues while held sends it again
// with the iteration one higher, and every target packet names an attack declared for its attack point, once
// (a bouncing arrow once per target it can reach).
public sealed class SkillHitBudget {
    private readonly Dictionary<byte, int> iterations = new();
    private readonly Dictionary<long, (byte AttackPoint, int Uses)> attacks = new();

    public bool TakePoint(byte attackPoint, int iteration, bool held, int count) {
        if (count > SkillHitRules.MaxPointAttacks) {
            return false;
        }
        if (iterations.TryGetValue(attackPoint, out int last)) {
            if (!held || iteration != last + 1) {
                return false;
            }
        } else if (iteration != 0) {
            return false;
        }

        iterations[attackPoint] = iteration;
        return true;
    }

    public void Declare(long attackUid, byte attackPoint) {
        attacks.TryAdd(attackUid, (attackPoint, 0));
    }

    public bool TakeTarget(long attackUid, byte attackPoint, int bounceCount) {
        if (!attacks.TryGetValue(attackUid, out (byte AttackPoint, int Uses) attack) || attack.AttackPoint != attackPoint) {
            return false;
        }
        if (attack.Uses >= 1 + Math.Max(0, bounceCount)) {
            return false;
        }

        attacks[attackUid] = (attackPoint, attack.Uses + 1);
        return true;
    }
}
