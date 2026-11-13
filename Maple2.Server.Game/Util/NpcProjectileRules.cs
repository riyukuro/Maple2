using System.Numerics;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Tools.Collision;

namespace Maple2.Server.Game.Util;

// An npc shot that flies along a straight magic path and hits what its arrow collision box sweeps on the way, as the
// client moves and tests it (CMagic, client/ms2/magicdata.cpp). The client sends the hit only for a shot of its own
// player, so an npc's shot is swept here.
// - Timing (FUN_14145de80): the shot waits the path's delayTime and spawnTime after the target packet, then flies;
//   a lifeTime ends it early, without a hit test in that update.
// - Start (FUN_1414609e0): the caster's position plus fireOffset, turned to the cast direction when the path rotates,
//   and by -rotateZDegree either way. The client starts at the caster's fire node (its model's node position); the
//   server has no node positions and starts at the feet.
// - Targets: each path of the attack takes its own target of the target packet, in order, and its record names the
//   path the client draws (PathRecord). Every path is fired, also one without a target.
// - Once spawned the shot flies on when the caster dies or is removed (it needs the caster only at the start); a
//   homing shot whose target dies flies on to the target's last point. A shot ends with its field.
// - Movement (FUN_14145ec50): a straight line at the path's velocity to its end: the target actor's position when the
//   target packet names one (its middle, followed each step), else start + direction × distance + controlEndOffset.
// - Box (FUN_141465b30 with the arrow row): from the last position to the new one, collision.X wide, from
//   -collisionAdd.Y behind the last position to collision.Y past the new one, from -collisionAdd.Z below the step to
//   collision.Z above it. It is tested against each actor's hit box (CBaseCollision::Detect): a player's is the
//   client's PC box.
// - Hits: bounceType 3 and 4 pierce, up to max(1, bounceCount) different actors, each once (again after each
//   piercingAttackInterval), at most piercingAttackMaxTargetCount between two intervals; bounceType 2 hits only
//   where the flight ends; any other stops the shot at its first hit. nonTarget 0 keeps only the caster.
public static class NpcProjectileRules {
    // The server steps a flying shot this often: chosen here.
    public static readonly TimeSpan Step = TimeSpan.FromMilliseconds(100);

    // Whether the attack's shot along the path is swept here: the path flies a straight line (no curve control
    // points), starts at the caster (no fireFixed point), the arrow has a collision box that can hit another actor,
    // and the box can reach a player standing level with the caster somewhere along the line (ReachesGround).
    public static bool Sweeps(SkillMetadataArrow arrow, MagicPath path) {
        return NpcSplashRules.Flies(path)
               && path.ControlValue0 == Vector3.Zero && path.ControlValue1 == Vector3.Zero && !path.CatmullRom
               && path.FireFixed == Vector3.Zero
               && arrow.NonTarget != SkillTargetType.None
               && arrow.Collision.X > 0 && arrow.Collision.Z + arrow.CollisionAdd.Z > 0
               && ReachesGround(arrow, path);
    }

    // Whether an attack's shots are swept: every path of it flies and sweeps. An attack with a path that does not
    // fly or sweep keeps its hit in its range at the cast (chosen here), so no path loses its hit and no target is
    // hit twice.
    public static bool Swept(SkillMetadataArrow arrow, IReadOnlyList<MagicPath> paths) {
        return paths.Count > 0 && paths.All(path => Sweeps(arrow, path));
    }

    // Whether the box, from the start to the end of the line, overlaps the height of a player's box standing level
    // with the caster (0 to PlayerHeight above the caster's feet). The fire node the client starts from is unknown
    // here, so a path that starts or ends well above the caster cannot be told from one drawn there; those keep
    // their range hit (chosen here: 127 fixed and 2 homing npc paths in KR).
    public static bool ReachesGround(SkillMetadataArrow arrow, MagicPath path) {
        Vector3 direction = path.Direction.LengthSquared() > 1e-6f ? Vector3.Normalize(path.Direction) : Vector3.UnitY;
        float startZ = path.FireOffset.Z;
        float endZ = startZ + direction.Z * path.Distance + path.ControlEndOffsetValue.Z;
        return MathF.Min(startZ, endZ) - arrow.CollisionAdd.Z <= PlayerHeight
               && MathF.Max(startZ, endZ) + arrow.Collision.Z >= 0;
    }

    // A path's record of the target packet. Its fifth field (Unknown) is the path the client draws: it reads it
    // as m_n84 (CSceneStateGame::FUN_14171c1d0, scenestategame.cpp:5546) and CMagicModel::Init takes path
    // m_n84 % count of the attack's magic path list (FUN_14145c300, magicdata.cpp:287); the client's own picker
    // writes each record's running index there (PickAttackTargets, stateskill.cpp:2609). Uid 2 + index, as before.
    public static Maple2.Server.Game.Model.Skill.TargetRecord PathRecord(int pathIndex, int targetId) {
        return new Maple2.Server.Game.Model.Skill.TargetRecord {
            Uid = 2 + pathIndex,
            TargetId = targetId,
            Unknown = (byte) pathIndex,
        };
    }

    // The records of a bounce chain after a path's record, as the client links them (StateSkill FUN_1417f3e80 for its
    // own casts; the target packet FUN_14171c1d0 hangs a record under the one its PrevUid names and draws a shot from
    // it): one per chained object, each linked to the one before, its index the bounce number (1 for the first
    // bounce). Uids run on from firstUid.
    public static List<Maple2.Server.Game.Model.Skill.TargetRecord> ChainRecords(Maple2.Server.Game.Model.Skill.TargetRecord root,
            IReadOnlyList<int> chainedIds, long firstUid) {
        var records = new List<Maple2.Server.Game.Model.Skill.TargetRecord>();
        Maple2.Server.Game.Model.Skill.TargetRecord last = root;
        for (int i = 0; i < chainedIds.Count; i++) {
            var record = new Maple2.Server.Game.Model.Skill.TargetRecord {
                PrevUid = last.Uid,
                Uid = firstUid + i,
                TargetId = chainedIds[i],
                Index = (byte) (i + 1),
            };
            records.Add(record);
            last = record;
        }
        return records;
    }

    // The target of the target packet each path of an attack takes: the path's own index when there is one, else
    // none (-1). A path homes on its target only for an overlap arrow.
    public static int[] PathTargets(int pathCount, int targetCount) {
        var result = new int[pathCount];
        for (int i = 0; i < pathCount; i++) {
            result[i] = i < targetCount ? i : -1;
        }
        return result;
    }

    public static bool Pierces(SkillMetadataArrow arrow) => arrow.BounceType is BounceType.Pierce or BounceType.Boomerang;

    public static int MaxHits(SkillMetadataArrow arrow) => Math.Max(1, arrow.BounceCount);

    // The wait between the target packet and the first step.
    public static TimeSpan Delay(MagicPath path) => TimeSpan.FromSeconds(Math.Max(0, path.DelayTime) + Math.Max(0, path.SpawnTime));

    // The shot's forward on the ground: the cast direction for a rotating path, else +Y; either turned by
    // rotateZDegree (FUN_1414609e0 turns the direction by -rotZ in both cases).
    public static Vector3 Forward(Vector3 castDirection, MagicPath path) {
        var flat = path.Rotate ? new Vector2(castDirection.X, castDirection.Y) : Vector2.UnitY;
        if (flat.LengthSquared() < 1e-6f) {
            flat = Vector2.UnitY;
        }
        flat = Vector2.Normalize(flat);
        if (path.RotateZDegree != 0) {
            flat = Vector2.Transform(flat, Matrix3x2.CreateRotation(-path.RotateZDegree * MathF.PI / 180));
        }
        return new Vector3(flat, 0);
    }

    // A path-space vector (x to the side, y forward, z up) in the world for the forward, by the server's rotation
    // convention (+Y is the facing): x·(-forward.Y, forward.X) + y·forward.
    public static Vector3 Local(Vector3 forward, Vector3 value) {
        var side = new Vector3(-forward.Y, forward.X, 0);
        return side * value.X + forward * value.Y + new Vector3(0, 0, value.Z);
    }

    public static Vector3 Start(Vector3 caster, Vector3 forward, MagicPath path) => caster + Local(forward, path.FireOffset);

    // The end of a shot without a target actor.
    public static Vector3 End(Vector3 start, Vector3 forward, MagicPath path) {
        Vector3 direction = path.Direction.LengthSquared() > 1e-6f ? Vector3.Normalize(path.Direction) : Vector3.UnitY;
        return start + Local(forward, direction) * path.Distance + path.ControlEndOffsetValue;
    }

    // One step toward the goal at the velocity; true once it is reached.
    public static (Vector3 Position, bool Arrived) Move(Vector3 current, Vector3 goal, float velocity, float seconds) {
        Vector3 offset = goal - current;
        float length = offset.Length();
        float step = velocity * seconds;
        if (length <= step || length < 1e-3f) {
            return (goal, true);
        }
        return (current + offset / length * step, false);
    }

    // A shot that follows a target stops once it is the path's distance from its start on the ground: chosen here,
    // as the client stops a player or pet's shot there (owner types 1 and 3), so a shot cannot chase for good.
    public static bool PastDistance(Vector3 start, Vector3 current, MagicPath path) {
        return new Vector2(current.X - start.X, current.Y - start.Y).Length() >= path.Distance;
    }

    // The box the shot sweeps from one position to the next. A step that does not move faces the forward.
    public static Prism SweepBox(Vector3 from, Vector3 to, Vector3 forward, SkillMetadataArrow arrow) {
        var travel = new Vector2(to.X - from.X, to.Y - from.Y);
        float length = travel.Length();
        Vector2 facing = length > 1e-3f ? travel / length : Vector2.Normalize(new Vector2(forward.X, forward.Y));
        float back = arrow.CollisionAdd.Y;
        float boxLength = MathF.Max(1e-3f, length + arrow.Collision.Y + back);
        Vector2 center = new Vector2(from.X, from.Y) + facing * ((length + arrow.Collision.Y - back) / 2);
        float angle = MathF.Atan2(-facing.X, facing.Y) * 180 / MathF.PI;
        // The client tilts the box with the travel; here it stands upright over both ends of the step (chosen here).
        float bottom = MathF.Min(from.Z, to.Z) - arrow.CollisionAdd.Z;
        float top = MathF.Max(from.Z, to.Z) + arrow.Collision.Z;
        return new Prism(new Rectangle(center, arrow.Collision.X, boxLength, angle), bottom, top - bottom);
    }

    // A player's hit box: the client's default PC box, 70 by 70 around the position and 150 high
    // (PC::SetDefaultBoundingBox, size (70, 35, 150), offset (0, 35, 0)). It does not turn with the player here.
    public const float PlayerWidth = 70f;
    public const float PlayerHeight = 150f;

    public static Prism PlayerBox(Vector3 position) {
        return new Prism(new Rectangle(new Vector2(position.X, position.Y), PlayerWidth, PlayerWidth, 0), position.Z, PlayerHeight);
    }

    // The box widened so a pool query against the server's shapes (a player's is 100 high) finds every actor whose
    // hit box the box can touch.
    public static Prism QueryBox(Prism box) {
        return new Prism(box.Polygon, box.Height.Min - PlayerHeight, box.Height.Max - box.Height.Min + 2 * PlayerHeight);
    }

    // The point a shot follows on its target: the middle of its height (chosen here, for the client's hit point or
    // position + extent, CMagic::UpdateTargetPosition).
    public static Vector3 AimPoint(Vector3 position, float height) => position + new Vector3(0, 0, height / 2);

    // How far along the travel a point is, to take the nearest hit first.
    public static float Along(Vector3 from, Vector3 forward, Vector3 point) {
        return (point.X - from.X) * forward.X + (point.Y - from.Y) * forward.Y;
    }

    // The flight of one shot, one Step at a time from its spawn (Delay after the target packet).
    public sealed class Flight {
        private readonly Vector3 start;
        private readonly MagicPath path;
        private readonly bool homes;
        private readonly long lifeMs;
        private Vector3 goal;

        // end: where a shot without a target flies; a homing shot flies to the point Next is given.
        public Flight(Vector3 start, Vector3 end, MagicPath path, bool homes) {
            this.start = start;
            this.path = path;
            this.homes = homes;
            goal = end;
            Position = start;
            lifeMs = path.LifeTime > 0 ? (long) Math.Round(path.LifeTime * 1000) : long.MaxValue;
        }

        public Vector3 Position { get; private set; }
        public long ElapsedMs { get; private set; }
        public bool Ended { get; private set; }

        // The next step, from the last position to the new one, or null once the flight ended (it reached its goal
        // or its distance, or its lifeTime passed: that update tests nothing). target: the homing target's aim point
        // now, null once it is lost, so the shot flies on to the last one.
        public (Vector3 From, Vector3 To, bool Arrived)? Next(Vector3? target) {
            if (Ended) {
                return null;
            }
            if (homes && target is Vector3 point) {
                goal = point;
            }

            ElapsedMs += (long) Step.TotalMilliseconds;
            if (ElapsedMs >= lifeMs) {
                Ended = true;
                return null;
            }

            Vector3 from = Position;
            (Vector3 next, bool arrived) = Move(from, goal, path.Velocity, (float) Step.TotalSeconds);
            if (homes && PastDistance(start, next, path)) {
                arrived = true;
            }
            Position = next;
            Ended = arrived;
            return (from, next, arrived);
        }
    }

    // The hit bookkeeping of one shot.
    public sealed class Hits {
        private readonly SkillMetadataArrow arrow;
        private readonly int interval;
        private readonly HashSet<int> distinct = [];
        private readonly HashSet<int> excluded = [];
        private readonly int maxPerInterval;
        private long intervalStart;

        public Hits(SkillMetadataArrow arrow, MagicPath path) {
            this.arrow = arrow;
            interval = Pierces(arrow) ? path.PiercingAttackInterval : 0;
            maxPerInterval = Pierces(arrow) ? path.PiercingAttackMaxTargetCount : 0;
        }

        public bool Done { get; private set; }
        public int Count => distinct.Count;

        // The ids the shot cannot hit now.
        public IReadOnlySet<int> Excluded(long elapsedMs) {
            if (interval > 0 && elapsedMs - intervalStart >= interval) {
                intervalStart = elapsedMs;
                excluded.Clear();
            }
            return excluded;
        }

        // Takes the ids the box found (nearest first, none excluded) and gives the ones hit this step.
        public List<int> Take(IReadOnlyList<int> found, bool arrived) {
            var hit = new List<int>();
            if (Done || found.Count == 0) {
                return hit;
            }
            if (arrow.BounceType == BounceType.Chain && !arrived) {
                return hit;
            }

            int count = Math.Min(MaxHits(arrow) - distinct.Count, found.Count);
            if (Pierces(arrow) && maxPerInterval > 0 && excluded.Count + count > maxPerInterval) {
                count = Math.Max(0, maxPerInterval - excluded.Count);
            }
            for (int i = 0; i < count; i++) {
                hit.Add(found[i]);
                distinct.Add(found[i]);
                excluded.Add(found[i]);
            }
            if (!Pierces(arrow) && hit.Count > 0 || distinct.Count >= MaxHits(arrow)) {
                Done = true;
            }
            return hit;
        }
    }
}
