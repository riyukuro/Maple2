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

        // The attack's shots are swept when every flying path of it sweeps; one that mixes in a curve keeps the hit
        // in its range at the cast (chosen here), so no target is hit twice.
        bool swept = flies && paths!.Where(NpcSplashRules.Flies).All(path => NpcProjectileRules.Sweeps(attack.Arrow, path));
        var shots = new List<(MagicPath Path, IActor? Homing)>();
        if (attack.MagicPathId != 0) {
            if (actor.Field.TableMetadata.MagicPathTable.Entries.TryGetValue(attack.MagicPathId, out IReadOnlyList<MagicPath>? magicPaths)) {
                int targetIndex = 0;

                foreach (MagicPath path in magicPaths) {
                    int targetId = 0;

                    if (attack.Arrow.Overlap && attackTargets.Count > targetIndex) {
                        targetId = attackTargets[targetIndex].ObjectId;
                    }

                    var targets = new List<TargetRecord>();
                    var targetRecord = new TargetRecord {
                        Uid = 0 + 2 + targetIndex,
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
                    if (attackTargets.Count > targetIndex) {
                        // if attack.direction == 3, use direction to target, if attack.direction == 0, use rotation maybe?
                        cast.Position = actor.Position;
                        // A target on the caster's spot gives no direction: the cast keeps the one it has.
                        Vector3 offset = attackTargets[targetIndex].Position - actor.Position;
                        float length = offset.Length();
                        if (float.IsFinite(length) && length > 1e-3f) {
                            cast.Direction = offset / length;
                        }
                    }

                    actor.Field.Broadcast(SkillDamagePacket.Target(cast, targets));

                    if (swept && NpcSplashRules.Flies(path)) {
                        shots.Add((path, targetId != 0 ? attackTargets[targetIndex] : null));
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

        foreach ((MagicPath path, IActor? homing) in shots) {
            Fire(cast, path, homing);
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
    private void Fire(SkillRecord cast, MagicPath path, IActor? homing) {
        var shot = new SkillRecord(cast.Metadata, cast.CastUid, actor) {
            TargetUid = cast.TargetUid,
            ServerTick = cast.ServerTick,
            Position = cast.Position,
            CastPosition = cast.CastPosition,
            Direction = cast.Direction,
            Rotation = cast.Rotation,
            SplashPlaced = cast.SplashPlaced,
        };
        if (!shot.TrySetMotionPoint(cast.MotionPoint) || !shot.TrySetAttackPoint(cast.AttackPoint)) {
            return;
        }

        SkillMetadataAttack attack = shot.Attack;
        SkillMetadataArrow arrow = attack.Arrow;
        FieldManager field = actor.Field;
        Vector3 forward = NpcProjectileRules.Forward(cast.Direction, path);
        Vector3 start = NpcProjectileRules.Start(actor.Position, forward, path);
        Vector3 end = NpcProjectileRules.End(start, forward, path);
        var hits = new NpcProjectileRules.Hits(arrow, path);
        var hitActors = new Dictionary<int, IActor>();
        float seconds = (float) NpcProjectileRules.Step.TotalSeconds;
        long lifeMs = path.LifeTime > 0 ? (long) (path.LifeTime * 1000) : long.MaxValue;
        Vector3 current = start;
        long elapsedMs = 0;

        void Tick() {
            if (actor.IsDead || actor.Field != field) {
                return;
            }

            elapsedMs += (long) NpcProjectileRules.Step.TotalMilliseconds;
            Vector3 goal = end;
            if (homing != null) {
                if (homing.IsDead || homing.Field != field) {
                    return;
                }
                goal = NpcProjectileRules.AimPoint(homing.Position, HitHeight(homing));
            }

            (Vector3 next, bool arrived) = NpcProjectileRules.Move(current, goal, path.Velocity, seconds);
            if (homing != null && NpcProjectileRules.PastDistance(start, next, path)) {
                arrived = true;
            }
            bool last = arrived || elapsedMs >= lifeMs;

            IReadOnlySet<int> excluded = hits.Excluded(elapsedMs);
            Tools.Collision.Prism box = NpcProjectileRules.SweepBox(current, next, forward, arrow);
            Vector3 from = current;
            Vector3 travel = next - current;
            Vector3 facing = travel.LengthSquared() > 1e-6f ? Vector3.Normalize(travel) : forward;
            List<IActor> found = field.GetTargets(actor, [NpcProjectileRules.QueryBox(box)], attack.Range, int.MaxValue)
                .Where(target => target.ObjectId != actor.ObjectId && !excluded.Contains(target.ObjectId))
                .Where(target => box.Intersects(HitBox(target)))
                .OrderBy(target => NpcProjectileRules.Along(from, facing, target.Position))
                .ToList();
            foreach (IActor target in found) {
                hitActors[target.ObjectId] = target;
            }
            List<int> hit = hits.Take(found.Select(target => target.ObjectId).ToList(), last);
            current = next;

            if (hit.Count > 0) {
                shot.Targets.Clear();
                foreach (int id in hit) {
                    shot.Targets.TryAdd(id, hitActors[id]);
                }
                shot.ImpactPosition = next;
                actor.TargetAttack(shot);
            }

            if (!last && !hits.Done) {
                field.Scheduler.Schedule(Tick, NpcProjectileRules.Step);
            }
        }

        field.Scheduler.Schedule(Tick, NpcProjectileRules.Delay(path));
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
