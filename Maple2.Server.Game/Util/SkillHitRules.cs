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
    // KMS2 attack point packets declare 0, 1, 3, 4, 10 or 20 attacks, and never more than the attack's target count;
    // an attack whose arrow needs no target (NonTarget 1) declares one with a target count of 0.
    public const int MaxPointAttacks = 20;
    // A held skill sends its attack point again each time its motion's loop comes round: every
    // (loopend - loopstart) / speed, with the speed of SkillCastRules.MotionSpeed. In KMS2 the iterations of
    // a 500 to 600 ms loop arrive 0.91 to 1.02 times that apart; those of a 250 ms loop arrive 270 ms apart in the
    // middle, single ones 51 to 579 ms apart (a burst after a late one).
    // Chosen here: the share of the loop time and the time under it that an iteration may come early.
    public const float HeldIntervalShare = 0.9f;
    public const long HeldIntervalSlackMs = 20;
    // Chosen here: an iteration may arrive this long before its time. The times are counted on from the first
    // iteration, one interval each, so a hold as a whole is not faster than its loop, and iterations that arrive
    // together after a late one (a stall of the link or of the field) are still on time.
    public const long HeldArrivalAllowanceMs = 500;
    // KMS2 sends the last attack packet of a cast at most 3.55 s after the cast (99 in 100 within 0.82 s). A skill
    // that continues while held counts from its last taken attack point packet instead (SkillHitBudget).
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

    public static bool TooLate(long tick, long windowTick) => tick - windowTick > HitWindowMs;

    // The most attacks an attack point packet declares for an attack.
    public static int MaxAttacks(int targetCount, bool nonTarget) {
        return Math.Min(MaxPointAttacks, Math.Max(targetCount, nonTarget ? 1 : 0));
    }

    // The shortest time between two iterations of a held skill; 0 for a motion without a loop (not checked).
    public static long HeldInterval(float loopSeconds, float speed) {
        if (loopSeconds <= 0 || speed <= 0) {
            return 0;
        }
        return Math.Max(0, (long) (loopSeconds / speed * 1000 * HeldIntervalShare) - HeldIntervalSlackMs);
    }

    // Whether an iteration arrives before its time.
    public static bool IterationTooEarly(long tick, long dueTick) => tick < dueTick - HeldArrivalAllowanceMs;
}

// What one cast may still hit. An attack point packet declares attacks by uid; a target packet pays damage for one
// declared attack. In KMS2 each attack point is sent once per cast, a skill that continues while held sends it again
// with the iteration one higher, and every target packet names an attack declared for its attack point, once
// (a bouncing arrow once per target it can reach). The time window for these packets starts at the cast; each taken
// iteration of a held skill starts it again, so a hold pays for as long as its iterations keep coming.
public sealed class SkillHitBudget {
    private long windowTick;
    private readonly Dictionary<byte, int> iterations = new();
    private readonly Dictionary<byte, long> dueTicks = new(); // when an attack point's next iteration is due
    private readonly Dictionary<long, (byte AttackPoint, int Uses)> attacks = new();

    // The cast: the time window starts here.
    public void Open(long tick) => windowTick = tick;

    public bool TooLate(long tick) => SkillHitRules.TooLate(tick, windowTick);

    // maxCount: the most attacks the packet may declare (SkillHitRules.MaxAttacks). intervalMs: the shortest time
    // between two iterations of a held skill (SkillHitRules.HeldInterval), 0 when not known.
    public bool TakePoint(byte attackPoint, int iteration, bool held, int count, long tick,
                          int maxCount = SkillHitRules.MaxPointAttacks, long intervalMs = 0) {
        if (TooLate(tick) || count > maxCount) {
            return false;
        }
        long due = tick;
        bool early = false;
        if (iterations.TryGetValue(attackPoint, out int last)) {
            if (!held || iteration != last + 1) {
                return false;
            }
            due = dueTicks.GetValueOrDefault(attackPoint, tick);
            early = SkillHitRules.IterationTooEarly(tick, due);
        } else if (iteration != 0) {
            return false;
        }

        // An iteration that only comes too early is counted and pays nothing: the ones after it go on.
        iterations[attackPoint] = iteration;
        dueTicks[attackPoint] = due + intervalMs;
        if (early) {
            return false;
        }
        if (held) {
            windowTick = tick;
        }
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
