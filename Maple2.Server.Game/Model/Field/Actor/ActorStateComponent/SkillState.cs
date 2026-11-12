using System.Numerics;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Game.Model.ActorStateComponent;

public class SkillState {
    private readonly IActor actor;

    public SkillState(IActor actor) {
        this.actor = actor;
    }

    public void SkillCastAttack(SkillRecord cast, byte attackPoint, List<IActor> attackTargets) {
        if (!cast.TrySetAttackPoint(attackPoint)) {
            return;
        }

        SkillMetadataAttack attack = cast.Attack;

        // Region skills that need no hit target (NpcSplashRules). A shot that carries one is aimed first, so the
        // target packet below gives its direction.
        SkillEffectMetadata[] noTarget = attack.Skills.Where(NpcSplashRules.NoTarget).ToArray();
        IReadOnlyList<MagicPath>? paths = null;
        bool flies = attack.MagicPathId != 0
                     && actor.Field.TableMetadata.MagicPathTable.Entries.TryGetValue(attack.MagicPathId, out paths)
                     && paths.Any(NpcSplashRules.Flies);
        if (noTarget.Length > 0 && flies) {
            attackTargets = Aim(attack, paths!, attackTargets);
        }

        // The attack's shots are swept when every path of it flies and sweeps (NpcProjectileRules.Swept).
        bool swept = flies && NpcProjectileRules.Swept(attack.Arrow, paths!);
        var shots = new List<(MagicPath Path, IActor? Homing, Vector3 Direction)>();
        if (attack.MagicPathId != 0) {
            if (actor.Field.TableMetadata.MagicPathTable.Entries.TryGetValue(attack.MagicPathId, out IReadOnlyList<MagicPath>? magicPaths)) {
                // Each path takes its own target of the list (NpcProjectileRules.PathTargets).
                int[] pathTargets = NpcProjectileRules.PathTargets(magicPaths.Count, attackTargets.Count);
                for (int pathIndex = 0; pathIndex < magicPaths.Count; pathIndex++) {
                    MagicPath path = magicPaths[pathIndex];
                    int targetIndex = pathTargets[pathIndex];
                    IActor? pathTarget = targetIndex >= 0 ? attackTargets[targetIndex] : null;
                    int targetId = 0;

                    if (attack.Arrow.Overlap && pathTarget != null) {
                        targetId = pathTarget.ObjectId;
                    }

                    var targets = new List<TargetRecord>();
                    var targetRecord = new TargetRecord {
                        Uid = 0 + 2 + pathIndex,
                        TargetId = targetId,
                        Unknown = 0,
                    };
                    targets.Add(targetRecord);

                    // TODO: chaining
                    // While chaining
                    // while (packet.ReadBool()) {
                    //     targetRecord = new TargetRecord {
                    //         PrevUid = targetRecord.Uid,
                    //         Uid = packet.ReadLong(),
                    //         TargetId = packet.ReadInt(),
                    //         Unknown = packet.ReadByte(),
                    //         Index = packet.ReadByte(),
                    //     };
                    //     targets.Add(targetRecord);
                    // }
                    if (pathTarget != null) {
                        // if attack.direction == 3, use direction to target, if attack.direction == 0, use rotation maybe?
                        cast.Position = actor.Position;
                        // A target on the caster's spot gives no direction: the cast keeps the one it has.
                        Vector3 offset = pathTarget.Position - actor.Position;
                        float length = offset.Length();
                        if (float.IsFinite(length) && length > 1e-3f) {
                            cast.Direction = offset / length;
                        }
                    }

                    actor.Field.Broadcast(SkillDamagePacket.Target(cast, targets));

                    // The shot flies in the direction this path's target packet sent.
                    if (swept) {
                        shots.Add((path, targetId != 0 ? pathTarget : null, cast.Direction));
                    }
                }
            }
        }

        // Apply damage to targets server-side for NPC attacks
        // Always use the attack range prism to resolve targets so spatial checks are respected
        Tools.Collision.Prism attackPrism = attack.Range.GetPrism(actor.Position, actor.Rotation.Z);
        var resolvedTargets = new List<IActor>();
        int queryLimit = attack.TargetCount > 0 ? attack.TargetCount : 1;
        foreach (IActor target in actor.Field.GetTargets(actor, [attackPrism], attack.Range, queryLimit)) {
            resolvedTargets.Add(target);
        }

        NpcSplashRules.Place place = NpcSplashRules.Where(noTarget.Length > 0, flies, resolvedTargets.Count);
        cast.SplashPlaced = place == NpcSplashRules.Place.Landing;
        if (place == NpcSplashRules.Place.Landing) {
            LandSplashes(noTarget, paths!, attackTargets);
        }

        foreach ((MagicPath path, IActor? homing, Vector3 direction) in shots) {
            Fire(cast, path, homing, direction);
        }

        // A swept shot hits what it passes (Fire), not the attack range at the caster.
        if (resolvedTargets.Count > 0 && !swept) {
            int limit = attack.TargetCount > 0 ? attack.TargetCount : 1;
            for (int i = 0; i < resolvedTargets.Count && i < limit; i++) {
                IActor target = resolvedTargets[i];
                cast.Targets.TryAdd(target.ObjectId, target);
            }

            // Reuse existing pipeline to calculate and broadcast damage/effects
            actor.TargetAttack(cast);
        }
    }

    // The actors the attack's shots are aimed at, one per path: the npc's own targets the first flying path
    // reaches, then the nearest other actors of the attack's target kind it reaches (chosen here). The npc itself
    // is never an aim.
    private List<IActor> Aim(SkillMetadataAttack attack, IReadOnlyList<MagicPath> paths, List<IActor> attackTargets) {
        MagicPath path = paths.First(NpcSplashRules.Flies);
        Vector3 from = actor.Position;
        List<IActor> aims = attackTargets
            .Where(target => target.ObjectId != actor.ObjectId && NpcSplashRules.Reaches(target.Position, from, path))
            .ToList();
        if (aims.Count >= paths.Count) {
            return aims;
        }

        float low = MathF.Min(0, path.ControlEndOffsetValue.Z) - AimHeight;
        var prism = new Tools.Collision.Prism(new Tools.Collision.Circle(new Vector2(from.X, from.Y), path.Distance + NpcSplashRules.AimSlack),
            from.Z + low, MathF.Abs(path.ControlEndOffsetValue.Z) + 2 * AimHeight);
        IEnumerable<IActor> pool = actor.Field.GetTargets(actor, [prism], attack.Range, int.MaxValue, aims)
            .Where(target => target.ObjectId != actor.ObjectId);
        aims.AddRange(NpcSplashRules.Aims(pool, target => target.Position, from, path).Take(paths.Count - aims.Count));
        return aims;
    }

    // One region per flying path: on the cell of the actor the shot is aimed at, else at the path's end.
    private void LandSplashes(SkillEffectMetadata[] effects, IReadOnlyList<MagicPath> paths, List<IActor> aims) {
        Vector3 from = actor.Position;
        Vector3 rotation = actor.Rotation;
        IActor caster = actor;
        FieldManager field = actor.Field;
        int index = 0;
        foreach (MagicPath path in paths) {
            if (!NpcSplashRules.Flies(path)) {
                index++;
                continue;
            }

            Vector3 point = aims.Count > index
                ? NpcSplashRules.LandPoint(aims[index].Position)
                : NpcSplashRules.EndPoint(from, rotation, path);
            index++;
            field.Scheduler.Schedule(() => {
                foreach (SkillEffectMetadata effect in effects) {
                    field.AddSkill(caster, effect, [point], rotation);
                }
            }, NpcSplashRules.Flight(from, point, path));
        }
    }

    // Flies one shot along a straight path and hits the actors its arrow box sweeps (NpcProjectileRules), with a
    // copy of the cast as it is now, so a later attack point of the cast does not change it.
    private void Fire(SkillRecord cast, MagicPath path, IActor? homing, Vector3 direction) {
        var shot = new SkillRecord(cast.Metadata, cast.CastUid, actor) {
            TargetUid = cast.TargetUid,
            ServerTick = cast.ServerTick,
            Position = cast.Position,
            CastPosition = cast.CastPosition,
            Direction = direction,
            Rotation = cast.Rotation,
            SplashPlaced = cast.SplashPlaced,
        };
        if (!shot.TrySetMotionPoint(cast.MotionPoint) || !shot.TrySetAttackPoint(cast.AttackPoint)) {
            return;
        }

        SkillMetadataAttack attack = shot.Attack;
        SkillMetadataArrow arrow = attack.Arrow;
        FieldManager field = actor.Field;
        Vector3 forward = NpcProjectileRules.Forward(direction, path);
        Vector3 start = NpcProjectileRules.Start(actor.Position, forward, path);
        Vector3 end = homing != null
            ? NpcProjectileRules.AimPoint(homing.Position, HitHeight(homing))
            : NpcProjectileRules.End(start, forward, path);
        var flight = new NpcProjectileRules.Flight(start, end, path, homing != null);
        var hits = new NpcProjectileRules.Hits(arrow, path);
        var hitActors = new Dictionary<int, IActor>();

        void Tick() {
            // The shot flies on when the caster dies; it ends with the caster gone from the field (chosen here: its
            // hit is the caster's attack).
            if (actor.Field != field) {
                return;
            }

            Vector3? aim = null;
            if (homing != null) {
                if (homing.IsDead || homing.Field != field) {
                    homing = null;
                } else {
                    aim = NpcProjectileRules.AimPoint(homing.Position, HitHeight(homing));
                }
            }
            if (flight.Next(aim) is not { } step) {
                return;
            }
            (Vector3 from, Vector3 next, bool arrived) = step;

            IReadOnlySet<int> excluded = hits.Excluded(flight.ElapsedMs);
            Tools.Collision.Prism box = NpcProjectileRules.SweepBox(from, next, forward, arrow);
            Vector3 travel = next - from;
            Vector3 facing = travel.LengthSquared() > 1e-6f ? Vector3.Normalize(travel) : forward;
            List<IActor> found = field.GetTargets(actor, [NpcProjectileRules.QueryBox(box)], attack.Range, int.MaxValue)
                .Where(target => target.ObjectId != actor.ObjectId && !excluded.Contains(target.ObjectId))
                .Where(target => box.Intersects(HitBox(target)))
                .OrderBy(target => NpcProjectileRules.Along(from, facing, target.Position))
                .ToList();
            foreach (IActor target in found) {
                hitActors[target.ObjectId] = target;
            }
            List<int> hit = hits.Take(found.Select(target => target.ObjectId).ToList(), arrived);

            if (hit.Count > 0) {
                shot.Targets.Clear();
                foreach (int id in hit) {
                    shot.Targets.TryAdd(id, hitActors[id]);
                }
                shot.ImpactPosition = next;
                actor.TargetAttack(shot);
            }

            if (!flight.Ended && !hits.Done) {
                field.Scheduler.Schedule(Tick, NpcProjectileRules.Step);
            }
        }

        // The shot spawns at the delay and first moves one step later.
        field.Scheduler.Schedule(Tick, NpcProjectileRules.Delay(path) + NpcProjectileRules.Step);
    }

    // An actor's hit box for a swept shot: a player's is the client's PC box, an npc's its shape.
    private static Tools.Collision.IPrism HitBox(IActor target) {
        return target is FieldPlayer ? NpcProjectileRules.PlayerBox(target.Position) : target.Shape;
    }

    private static float HitHeight(IActor target) {
        return target is FieldPlayer ? NpcProjectileRules.PlayerHeight : target.Shape.Height.Max - target.Shape.Height.Min;
    }

    // The height band above and below the shot's start and end an aim is looked for in: chosen here.
    private const float AimHeight = 300f;
}
