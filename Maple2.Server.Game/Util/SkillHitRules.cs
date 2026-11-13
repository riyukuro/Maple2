using System.Numerics;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Tools.Collision;

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
    // together after a late one (a stall of the link or of the field) are still on time. An iteration that comes
    // sooner still (the first one was held up, the next ones not) draws the rest from the session's LagBudget, and
    // the times after it come sooner by what was drawn.
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

    // How the client writes the hits of an attack's target packets (CMagic, magicdata.cpp:2063): an attack with
    // AttackMethodType 1 flies as an arrow (FUN_141465b30), any other is a region (FUN_141464d00). paths: the
    // attack's magic path moves, which hold the arrow's piercing.
    public static SkillHitShape Shape(SkillMetadataAttack attack, IReadOnlyList<MagicPath>? paths) {
        SkillMetadataArrow arrow = attack.Arrow;
        if (attack.AttackMethodType != 1) {
            // A region hits at most TargetCount objects (FUN_141769410), each once; without nonTarget 1
            // (SkillArrow::IsNonTargetOne) only the attack's own target, and an object a point group already hit
            // this cast is left out (CSkillRecord::HasTarget).
            return new SkillHitShape(false, arrow.NonTarget != SkillTargetType.Owner, Math.Max(0, attack.TargetCount), false, false, attack.PointGroup);
        }
        // An arrow hits at most max(1, bounceCount) objects, sending the number it hit before with each one; with
        // nonTarget 0 only the attack's own target. Bounce types 3 and 4 pierce: with a piercing interval the arrow
        // forgets what it pierced each interval and may hit it again (FUN_141466da0); without one at most
        // piercingAttackMaxTargetCount (when set). Bounce type 4 comes back once at its move's end and counts from
        // 0 again (FUN_141460700). Chosen here: the most lenient of the path's moves.
        int max = Math.Max(1, arrow.BounceCount);
        bool pierces = arrow.BounceType is BounceType.Pierce or BounceType.Boomerang;
        bool repierce = false;
        if (pierces && paths is { Count: > 0 }) {
            repierce = paths.Any(path => path.PiercingAttackInterval != 0);
            if (!repierce && paths.All(path => path.PiercingAttackMaxTargetCount != 0)) {
                max = Math.Min(max, paths.Max(path => path.PiercingAttackMaxTargetCount));
            }
        }
        return new SkillHitShape(true, arrow.NonTarget == SkillTargetType.None, max, arrow.BounceType == BounceType.Boomerang, repierce, 0);
    }

    // How many times an attack's arrow sends its packets: a boomerang forgets what it hit at its move's end and
    // sends them again on the way back (FUN_141460700); an arrow with a piercing interval forgets what it pierced
    // each interval (FUN_141466da0), so once more per interval of its flight: the longest of its paths' lifeTime and
    // its move (distance or nonTargetMoveDistance over velocity) with the hold at the move's end, both ways for a boomerang (the hit window when neither is set). Chosen here: at most
    // MaxTargetRounds.
    public const int MaxTargetRounds = 100;

    public static int TargetRounds(SkillMetadataAttack attack, IReadOnlyList<MagicPath>? paths) {
        if (attack.AttackMethodType != 1) {
            return 1;
        }
        int ways = attack.Arrow.BounceType == BounceType.Boomerang ? 2 : 1;
        if (attack.Arrow.BounceType is not (BounceType.Pierce or BounceType.Boomerang) || paths is not { Count: > 0 }) {
            return ways;
        }
        int interval = int.MaxValue;
        float flightMs = 0;
        foreach (MagicPath path in paths) {
            if (path.PiercingAttackInterval > 0) {
                interval = Math.Min(interval, path.PiercingAttackInterval);
            }
            float moveMs = path.Velocity > 0 ? MathF.Max(path.Distance, path.NonTargetMoveDistance) / path.Velocity * 1000 : 0;
            flightMs = MathF.Max(flightMs, MathF.Max(path.LifeTime * 1000, moveMs + Math.Max(0, path.MoveEndHoldDuration)));
        }
        if (interval == int.MaxValue) {
            return ways;
        }
        if (flightMs <= 0) {
            flightMs = HitWindowMs;
        }
        return (int) Math.Min(MaxTargetRounds, ways + MathF.Ceiling(ways * flightMs / interval));
    }

    // How far outside the client's own shape a target packet's object may stand and its points may lie (chosen here):
    // the server's player position is the client's detect point within 2 in KMS2 (the client syncs before it sends),
    // so most of it is the objects' movement between the client's frame and the server's.
    public const float TargetMargin = 150f;
    // A region's packet carries the owner's facing when it hit. In KMS2 it lies within 79 degrees of the player's
    // last synced facing (9947 packets), and a cast that turns (10200232, 10400041) strays up to 179 from the
    // cast's rotation. Chosen here: within this of either.
    public const float FacingMargin = 90f;
    // An arrow's packet carries the step it hit on: its last and its current point. KMS2 steps are at most 139 long
    // and 0.1 s of the path's velocity. Chosen here: a step of at most this long at the fastest of the paths.
    public const float StepSeconds = 0.25f;
    // Two packets of one arrow: its current point moves at most the velocity for the time between them and this
    // long more, for packets that arrive bunched after a late one (chosen here).
    public const long StepArrivalAllowanceMs = 500;

    // Whether a region packet's facing is near the cast's rotation or the player's synced facing (degrees).
    public static bool FacingNear(float facing, float castFacing, float syncedFacing) {
        return AngleBetween(facing, castFacing) <= FacingMargin || AngleBetween(facing, syncedFacing) <= FacingMargin;
    }

    private static float AngleBetween(float a, float b) {
        float d = (a - b) % 360;
        if (d < 0) {
            d += 360;
        }
        return d > 180 ? 360 - d : d;
    }

    // A region's area as the client searches it (FUN_141464d00): the attack's range at the owner's position, turned
    // to its facing, grown by TargetMargin on every side.
    public static Prism RegionArea(SkillMetadataAttack attack, Vector3 position, float facing) {
        SkillMetadataRange range = attack.Range;
        SkillMetadataRange grown = range.Type == SkillRegion.HoleCylinder
            ? range with {
                Width = MathF.Max(0, range.Width - TargetMargin),
                EndWidth = range.EndWidth + TargetMargin,
                Height = range.Height + TargetMargin,
                RangeAdd = range.RangeAdd + new Vector3(0, 0, TargetMargin),
            }
            : range with {
                Distance = range.Distance + TargetMargin,
                Width = range.Width + 2 * TargetMargin,
                EndWidth = range.EndWidth + 2 * TargetMargin,
                Height = range.Height + TargetMargin,
                RangeAdd = range.RangeAdd + new Vector3(0, TargetMargin, TargetMargin),
            };
        return grown.GetPrism(position, facing, range.ApplyTarget, attack.AttackMethodType != 1);
    }

    // An arrow's step as the client sweeps it (FUN_141465b30): the collision box from the last point to the current
    // one along the arrow's axis, grown by TargetMargin on every side.
    // A step without a horizontal move and without a horizontal axis stands along y (chosen here).
    public static Prism ArrowArea(SkillMetadataArrow arrow, Vector3 last, Vector3 current, Vector3 axis) {
        if (axis.X * axis.X + axis.Y * axis.Y < 1e-6f) {
            axis = Vector3.UnitY;
        }
        SkillMetadataArrow grown = arrow with {
            Collision = arrow.Collision + new Vector3(2 * TargetMargin, TargetMargin, TargetMargin),
            CollisionAdd = arrow.CollisionAdd + new Vector3(0, TargetMargin, TargetMargin),
        };
        return NpcProjectileRules.SweepBox(last, current, axis, grown);
    }

    // The fastest of the attack's paths; 0 when none moves.
    public static float Velocity(IReadOnlyList<MagicPath>? paths) {
        float velocity = 0;
        if (paths != null) {
            foreach (MagicPath path in paths) {
                velocity = MathF.Max(velocity, path.Velocity);
            }
        }
        return velocity;
    }

    // Whether an arrow's step is longer than one step of its fastest path (not checked when no path moves).
    public static bool StepTooLong(Vector3 last, Vector3 current, float velocity) {
        return velocity > 0 && Vector3.Distance(last, current) > velocity * StepSeconds + TargetMargin;
    }

    // Whether an arrow's point lies further from the cast than its paths fly: the longest path's distance (or
    // nonTargetMoveDistance) from its fire offset, and for a chain or range bounce each link's bounce radius (a link
    // starts where the one before it hit, BounceChain.PicksAround). Not checked when no path has a distance.
    public static bool ReachTooFar(Vector3 point, Vector3 cast, SkillMetadataArrow arrow, IReadOnlyList<MagicPath>? paths) {
        if (paths is not { Count: > 0 }) {
            return false;
        }
        float reach = 0;
        foreach (MagicPath path in paths) {
            reach = MathF.Max(reach, MathF.Max(path.Distance, path.NonTargetMoveDistance) + path.FireOffset.Length() + path.ControlEndOffsetValue.Length());
        }
        if (reach <= 0) {
            return false;
        }
        if (arrow.BounceType is BounceType.Chain or BounceType.Range) {
            reach += Math.Max(0, arrow.BounceCount) * Math.Max(0, arrow.BounceRadius);
        }
        return Vector3.Distance(point, cast) > reach + TargetMargin;
    }

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
    public static bool IterationTooEarly(long tick, long dueTick) => IterationEarlyBy(tick, dueTick) > 0;

    // The time an iteration arrives before its time by, past the allowance.
    public static long IterationEarlyBy(long tick, long dueTick) => Math.Max(0, dueTick - HeldArrivalAllowanceMs - tick);
}

// How the client writes an attack's hits (SkillHitRules.Shape). Arrow: each hit carries the number of objects the
// arrow hit before it; a region's carry 0. Targeted: only the attack's own target (the attack point packet's
// TargetId). Max: the most objects one attack hits. Returns: the arrow comes back once and counts from 0 again.
// Repierce: the arrow may hit an object again. PointGroup: a region's point group, 0 for none.
public readonly record struct SkillHitShape(bool Arrow, bool Targeted, int Max, bool Returns, bool Repierce, int PointGroup);

// Why a hit of a target packet pays nothing.
public enum HitRefusal {
    None,
    Undeclared, // no attack declared under the uid
    NotTarget, // a targeted attack naming another object
    Ordinal, // an arrow's hit with the wrong number before it, or a region's with any
    Again, // the attack hit the object before
    Full, // the attack hit its most objects
    PointGroup, // the point group hit the object this cast
    NoActor, // no actor the range takes (FieldManager.FindTargets)
    Facing, // a region packet's facing far from the cast's and the player's
    Step, // an arrow packet's step longer than its path moves in one
    Reach, // an arrow packet's point further from the cast than its paths fly
    Speed, // an arrow's point moved faster than its path between two packets
    Outside, // the object stands outside the attack's area (SkillHitRules.RegionArea, ArrowArea)
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
    private readonly Dictionary<long, Attack> attacks = new();
    // The objects each point group hit since the cast's last tick sync: (point group << 32) | object id.
    private readonly HashSet<long> pointGroupHits = new();

    private sealed class Attack(byte attackPoint, int targetId) {
        public readonly byte AttackPoint = attackPoint;
        public readonly int TargetId = targetId;
        public int Uses;
        public HashSet<int>? Hit;
        public bool Returned;
        public bool Stepped;
        public Vector3 Point;
        public long PointTick;
    }

    // The cast: the time window starts here.
    public void Open(long tick) => windowTick = tick;

    public bool TooLate(long tick) => SkillHitRules.TooLate(tick, windowTick);

    // What the last taken attack point packet would have been refused for with enforce set; null when nothing.
    public string? Unenforced { get; private set; }

    // maxCount: the most attacks the packet may declare (SkillHitRules.MaxAttacks). intervalMs: the shortest time
    // between two iterations of a held skill (SkillHitRules.HeldInterval), 0 when not known. lag: the session's
    // budget for an iteration that arrives early, none when null. enforce: false takes a packet that declares more
    // attacks than maxCount (never more than SkillHitRules.MaxPointAttacks) or whose iteration comes too early, and
    // names the reason in Unenforced.
    public bool TakePoint(byte attackPoint, int iteration, bool held, int count, long tick,
                          int maxCount = SkillHitRules.MaxPointAttacks, long intervalMs = 0, LagBudget? lag = null, bool enforce = true) {
        Unenforced = null;
        if (TooLate(tick)) {
            return false;
        }
        // More than any attack point declares: not a matter of timing, refused whatever enforce says.
        if (count > SkillHitRules.MaxPointAttacks) {
            return false;
        }
        if (count > maxCount) {
            if (enforce) {
                return false;
            }
            Unenforced = $"{count} attacks declared, at most {maxCount}";
        }
        long due = tick;
        bool early = false;
        if (iterations.TryGetValue(attackPoint, out int last)) {
            if (!held || iteration != last + 1) {
                return false;
            }
            due = dueTicks.GetValueOrDefault(attackPoint, tick);
            long earlyBy = SkillHitRules.IterationEarlyBy(tick, due);
            if (earlyBy > 0 && lag != null && lag.TryDraw(earlyBy, tick)) {
                due -= earlyBy;
            } else if (earlyBy > 0 && !enforce) {
                Unenforced = $"iteration {earlyBy} ms early";
                due -= earlyBy;
            } else {
                early = earlyBy > 0;
            }
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

    // targetId: the object the attack point packet names for the attack (the client's tag caster id).
    public void Declare(long attackUid, byte attackPoint, int targetId) {
        attacks.TryAdd(attackUid, new Attack(attackPoint, targetId));
    }

    // rounds: how many times the attack's arrow may send its 1 + bounceCount packets (SkillHitRules.TargetRounds).
    public bool TakeTarget(long attackUid, byte attackPoint, int bounceCount, int rounds = 1) {
        if (!attacks.TryGetValue(attackUid, out Attack? attack) || attack.AttackPoint != attackPoint) {
            return false;
        }
        if (attack.Uses >= (1 + Math.Max(0, bounceCount)) * Math.Max(1, rounds)) {
            return false;
        }

        attack.Uses++;
        return true;
    }

    // An arrow packet's current point for the attack's uid: false when it moved further from the one before than the
    // velocity covers in the time between them (SkillHitRules.StepArrivalAllowanceMs more). The point is kept either
    // way.
    public bool TakeStep(long attackUid, Vector3 point, long tick, float velocity) {
        if (!attacks.TryGetValue(attackUid, out Attack? attack)) {
            return false;
        }
        bool ok = !attack.Stepped || velocity <= 0
            || Vector3.Distance(attack.Point, point)
            <= velocity * (Math.Max(0, tick - attack.PointTick) + SkillHitRules.StepArrivalAllowanceMs) / 1000f + SkillHitRules.TargetMargin;
        attack.Stepped = true;
        attack.Point = point;
        attack.PointTick = tick;
        return ok;
    }

    // One hit of a target packet the attack's uid took (TakeTarget): objectId with the number the client sent with
    // it. A taken hit counts whether or not the field finds its object, as the client counted it.
    public HitRefusal TakeHit(long attackUid, int objectId, byte ordinal, in SkillHitShape shape) {
        if (!attacks.TryGetValue(attackUid, out Attack? attack)) {
            return HitRefusal.Undeclared;
        }
        if (shape.Targeted && objectId != attack.TargetId) {
            return HitRefusal.NotTarget;
        }
        HashSet<int> hit = attack.Hit ??= new HashSet<int>();
        if (!shape.Arrow) {
            if (ordinal != 0) {
                return HitRefusal.Ordinal;
            }
            if (hit.Contains(objectId)) {
                return HitRefusal.Again;
            }
            if (hit.Count >= shape.Max) {
                return HitRefusal.Full;
            }
            if (shape.PointGroup != 0 && !pointGroupHits.Add(((long) shape.PointGroup << 32) | (uint) objectId)) {
                return HitRefusal.PointGroup;
            }
            hit.Add(objectId);
            return HitRefusal.None;
        }

        if (ordinal != hit.Count) {
            if (!shape.Returns || attack.Returned || ordinal != 0) {
                return HitRefusal.Ordinal;
            }
            attack.Returned = true;
            hit.Clear();
        }
        if (hit.Count >= shape.Max) {
            return HitRefusal.Full;
        }
        if (!hit.Add(objectId) && !shape.Repierce) {
            return HitRefusal.Again;
        }
        return HitRefusal.None;
    }

    // The cast's tick sync: the client forgets what its point groups hit (CSkillRecord::SendSkill).
    public void Sync() => pointGroupHits.Clear();
}
