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

    // Whether a target packet's impact position drops the packet: past ImpactReach from the cast, except for an arrow
    // whose paths have a distance or a fixed fire point: its impact is the step's last point, held by its paths'
    // reach instead (ReachTooFar, which follows a fixed start and a boomerang coming back to its owner).
    public static bool ImpactRefused(Vector3 impact, Vector3 cast, Vector3 owner, SkillMetadataAttack attack, IReadOnlyList<MagicPath>? paths) {
        if (!ImpactTooFar(impact, cast, attack.Range.Distance)) {
            return false;
        }
        if (attack.AttackMethodType != 1 || paths is not { Count: > 0 }
            || !paths.Any(path => path.FireFixed != Vector3.Zero || path.Distance > 0 || path.NonTargetMoveDistance > 0)) {
            return true;
        }
        return ReachTooFar(impact, cast, attack.Arrow, paths, owner);
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

    // How many region path packets (Skill 1 2) an arrow sends: one when it ends (CMagic::FUN_14145f570, a boomerang
    // only at the end of its way back) and, with a piercing interval, one per interval of its flight both ways
    // (FUN_141466da0 sends at each multiple of the interval after its start, magicdata.cpp:2384: at most
    // floor(F/I); bounce types 3 and 4 only, :1880). Fewer than TargetRounds for a boomerang without an interval
    // (its target packets come again on the way back, its region path does not).
    public static int RegionPathRounds(SkillMetadataAttack attack, IReadOnlyList<MagicPath>? paths) {
        int rounds = TargetRounds(attack, paths);
        if (attack.AttackMethodType != 1 || attack.Arrow.BounceType is not (BounceType.Pierce or BounceType.Boomerang)
            || paths is not { Count: > 0 } || !paths.Any(path => path.PiercingAttackInterval > 0)) {
            return 1;
        }
        // TargetRounds is ways + ceil(ways * F / I); the end send replaces the ways.
        int ways = attack.Arrow.BounceType == BounceType.Boomerang ? 2 : 1;
        return Math.Max(1, rounds - ways + 1);
    }

    // The hit count past which a piercing arrow sends no interval region path (CMagic::FUN_141465b30 returns before
    // FUN_141466da0 once the different objects it hit, m_map1D8, reach max(1, m_n110), the bounce count;
    // magicdata.cpp:1877-1884). 0 when there is no such cap: a boomerang clears the map on its way back
    // (FUN_141460700) and resumes, which the server only sees from later hits.
    public static int RegionPathHitCap(SkillMetadataAttack attack) {
        return attack.AttackMethodType == 1 && attack.Arrow.BounceType == BounceType.Pierce ? Math.Max(1, attack.Arrow.BounceCount) : 0;
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
    // and 0.1 s of the path's velocity, but the client cuts a step (the time since its last frame) only at the flight
    // time left (magicdata.cpp:976-984; a homing path's is its target's distance, :1490), so a frame hitch makes one
    // as long as the hitch, up to the rest of the flight. Chosen here: a step longer than this at the
    // fastest of the paths is logged, never refused (both ends are held by ImpactTooFar and ReachTooFar).
    public const float StepSeconds = 0.25f;
    // Two packets of one arrow: its current point moves at most the velocity for the time between their arrivals
    // and this long more. Arrival times are the server's, so a packet held up by the link makes the next one look
    // fast: logged, never refused (chosen here).
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
    // to its facing, grown by TargetMargin on every side. reach: the cast's key move reach (SkillUtils.KeyMoveReach),
    // which the client's search adds to a box or frustum's depth as it does for every PickAttackTargets search.
    public static Prism RegionArea(SkillMetadataAttack attack, Vector3 position, float facing, float reach = 0) {
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
        return grown.GetPrism(position, facing, range.ApplyTarget, attack.AttackMethodType != 1, reach);
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

    // Whether an arrow's step is longer than one step of its fastest path (not checked when no path moves). Log only:
    // a frame hitch makes such a step.
    public static bool StepTooLong(Vector3 last, Vector3 current, float velocity) {
        return velocity > 0 && Vector3.Distance(last, current) > velocity * StepSeconds + TargetMargin;
    }

    // Whether an arrow's point lies further from the cast than its paths fly: the longest path's distance (or
    // nonTargetMoveDistance) from its fire offset, and for a chain or range bounce each link's bounce radius (a link
    // starts where the one before it hit, BounceChain.PicksAround). Not checked when no path has a distance, or when
    // a path fires from a fixed world point (the client's start is that point, magicdata.cpp:1411). Measured from the
    // nearer of the cast and the owner's position now: the client starts a magic at the owner's fire node when it
    // spawns (:1408), after any dash or leap of the motion, and a boomerang's way back homes on its owner (:1386).
    public static bool ReachTooFar(Vector3 point, Vector3 cast, SkillMetadataArrow arrow, IReadOnlyList<MagicPath>? paths, Vector3 owner) {
        if (paths is not { Count: > 0 }) {
            return false;
        }
        float reach = 0;
        foreach (MagicPath path in paths) {
            if (path.FireFixed != Vector3.Zero) {
                return false;
            }
            reach = MathF.Max(reach, MathF.Max(path.Distance, path.NonTargetMoveDistance) + path.FireOffset.Length() + path.ControlEndOffsetValue.Length());
        }
        if (reach <= 0) {
            return false;
        }
        if (arrow.BounceType is BounceType.Chain or BounceType.Range) {
            reach += Math.Max(0, arrow.BounceCount) * Math.Max(0, arrow.BounceRadius);
        }
        return MathF.Min(Vector3.Distance(point, cast), Vector3.Distance(point, owner)) > reach + TargetMargin;
    }

    // What an arrow packet's points refuse its hits for: only a current point past the paths' reach (ReachTooFar).
    // The step's length (StepTooLong) and the speed since the packet before (SkillHitBudget.TakeStep) are logged by
    // the handler, never refused: a frame hitch and a held-up packet make both.
    public static HitRefusal ArrowRefusal(Vector3 current, Vector3 cast, Vector3 owner, SkillMetadataArrow arrow, IReadOnlyList<MagicPath>? paths) {
        return ReachTooFar(current, cast, arrow, paths, owner) ? HitRefusal.Reach : HitRefusal.None;
    }

    // How far from the server's player position a region packet's owner position is trusted. The client syncs the
    // player's position right before it writes the packet (MyPC::FUN_14152e0c0, magicdata.cpp:1840), so a legit one is
    // within a few units; the slack keeps a late sync and a mount's offset from its rider (:1837).
    public const float RegionOwnerTrust = 2 * TargetMargin;

    // Where a region's area stands: the owner's position the packet carries (impactPosition2, magicdata.cpp:1831),
    // when it lies within RegionOwnerTrust of the server's player position; the server's position otherwise.
    public static Vector3 RegionCentre(Vector3 owner, Vector3 player) {
        return RegionOwnerTrusted(owner, player) ? owner : player;
    }

    public static bool RegionOwnerTrusted(Vector3 owner, Vector3 player) {
        return Vector3.Distance(owner, player) <= RegionOwnerTrust;
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
    Ordinal, // a region's hit with a number before it (only an arrow's carry one)
    Again, // the attack hit the object before
    Full, // the attack hit its most objects
    PointGroup, // the point group hit the object this cast
    NoActor, // no actor the range takes (FieldManager.FindTargets)
    Facing, // a region packet's facing far from the cast's and the player's
    Reach, // an arrow packet's point further from the cast than its paths fly
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
    // The arrows attack point packets declared, by (attack point, send count, arrow index), and the region path
    // packets each key sent.
    private readonly Dictionary<(byte, int, int), int> arrows = new();
    private readonly Dictionary<(byte, int, int), int> regionPaths = new();
    // The attack uids each key declared, and the region paths the key had sent when all of them reached the hit cap.
    private readonly Dictionary<(byte, int, int), List<long>> arrowAttacks = new();
    private readonly Dictionary<(byte, int, int), int> cappedAt = new();

    private sealed class Attack(byte attackPoint, int targetId) {
        public readonly byte AttackPoint = attackPoint;
        public readonly int TargetId = targetId;
        public int Uses;
        public HashSet<int>? Hit;
        public int Next; // the number the arrow's next hit carries at least
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

    // How many iterations of the attack point were counted (0 before its first packet).
    public int PointIterations(byte attackPoint) => iterations.TryGetValue(attackPoint, out int last) ? last + 1 : 0;

    // targetId: the object the attack point packet names for the attack (the client's tag caster id).
    public void Declare(long attackUid, byte attackPoint, int targetId) {
        attacks.TryAdd(attackUid, new Attack(attackPoint, targetId));
    }

    // An arrow the attack point packet declares: the tag's index among the attack's (SkillTargetRecord::m_n84,
    // StateSkill::PickAttackTargets numbers them from 0; a bounce's linked tag carries 0) at the cast's send count
    // the packet carries (both are what the tag's Init took, so the region path packet the arrow sends carries them
    // too, magicdata.cpp:1161).
    // attackUid: the tag's uid (Declare), whose hits tell when a piercing arrow stops its interval sends.
    public void DeclareArrow(byte attackPoint, int sendCount, int arrow, long attackUid = 0) {
        var key = (attackPoint, sendCount, arrow);
        arrows[key] = arrows.GetValueOrDefault(key) + 1;
        if (!arrowAttacks.TryGetValue(key, out List<long>? uids)) {
            arrowAttacks[key] = uids = new List<long>();
        }
        uids.Add(attackUid);
    }

    // A region path packet (Skill 1 2): false when its cast is past the hit window, when no attack point packet
    // declared its arrow, or when the arrows declared under its key already sent rounds each: an arrow
    // sends one when it ends (CMagic::FUN_14145f570) and a piercing one per interval (FUN_141466da0, bounce types 3
    // and 4 only, magicdata.cpp:1880), so SkillHitRules.RegionPathRounds. hitCap (SkillHitRules.RegionPathHitCap,
    // 0 for none): once every arrow under the key hit that many different objects (the target packets that did so
    // came before, the client sends them first), each sends only its end one more. Counted only when taken.
    public bool TakeRegionPath(byte attackPoint, int sendCount, int arrow, int rounds, long tick, int hitCap = 0) {
        if (TooLate(tick)) {
            return false;
        }
        var key = (attackPoint, sendCount, arrow);
        if (!arrows.TryGetValue(key, out int declared)) {
            return false;
        }
        int used = regionPaths.GetValueOrDefault(key);
        int allowed = declared * Math.Max(1, rounds);
        if (hitCap > 0 && rounds > 1) {
            if (!cappedAt.ContainsKey(key) && arrowAttacks.TryGetValue(key, out List<long>? uids)
                && uids.All(uid => attacks.TryGetValue(uid, out Attack? attack) && attack.Hit?.Count >= hitCap)) {
                cappedAt[key] = used;
            }
            if (cappedAt.TryGetValue(key, out int capped)) {
                allowed = Math.Min(allowed, capped + declared);
            }
        }
        if (used >= allowed) {
            return false;
        }
        regionPaths[key] = used + 1;
        return true;
    }

    // rounds: how many times the attack's arrow may send its 1 + bounceCount packets (SkillHitRules.TargetRounds).
    // hits: the packet's hit count; a packet without hits (the client clamps a crowd to piercingAttackMaxTargetCount
    // and still sends, magicdata.cpp:1925) pays nothing and uses up nothing.
    public bool TakeTarget(long attackUid, byte attackPoint, int bounceCount, int rounds = 1, int hits = 1) {
        if (!attacks.TryGetValue(attackUid, out Attack? attack) || attack.AttackPoint != attackPoint) {
            return false;
        }
        if (hits <= 0) {
            return true;
        }
        if (attack.Uses >= (1 + Math.Max(0, bounceCount)) * Math.Max(1, rounds)) {
            return false;
        }

        attack.Uses++;
        return true;
    }

    // An arrow packet's current point for the attack's uid: false when it moved further from the one before than the
    // velocity covers in the time between their arrivals (SkillHitRules.StepArrivalAllowanceMs more). The point is
    // kept either way. Log only: a packet the link held up makes the next one look fast.
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

        // The client's number is the count of different objects the arrow hit before (m_map1D8), never its most. It
        // only tells a boomerang's way back (both maps cleared, FUN_141460700): the first number below the one the
        // hit before left. A number off the server's count is not refused: a packet the server dropped (late, or
        // past the packet cap) held the hits between, and the number cannot tell that from a cheat; Max and Again
        // bound what the arrow pays.
        if (ordinal >= shape.Max) {
            return HitRefusal.Full;
        }
        if (ordinal < attack.Next && shape.Returns && !attack.Returned) {
            attack.Returned = true;
            hit.Clear();
        }
        if (hit.Count >= shape.Max) {
            return HitRefusal.Full;
        }
        bool added = hit.Add(objectId);
        if (!added && !shape.Repierce && shape.Returns && !attack.Returned) {
            // A boomerang that hits an object again without re-piercing is on its way back, though the number did
            // not show it (the packets that started the way back were dropped).
            attack.Returned = true;
            hit.Clear();
            added = hit.Add(objectId);
        }
        if (!added && !shape.Repierce) {
            return HitRefusal.Again;
        }
        attack.Next = ordinal + (added ? 1 : 0);
        return HitRefusal.None;
    }

    // The cast's tick sync: the client forgets what its point groups hit (CSkillRecord::SendSkill).
    public void Sync() => pointGroupHits.Clear();
}
