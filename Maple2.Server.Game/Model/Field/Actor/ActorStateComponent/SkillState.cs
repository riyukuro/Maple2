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
        } else if (place == NpcSplashRules.Place.Caster) {
            cast.Position = actor.Position;
            cast.Rotation = actor.Rotation;
            if (attack.CubeMagicPathId != 0) {
                actor.Field.AddSkill(cast);
            } else {
                foreach (SkillEffectMetadata effect in noTarget) {
                    actor.Field.AddSkill(actor, effect, [actor.Position], actor.Rotation);
                }
            }
        }

        if (resolvedTargets.Count > 0) {
            int limit = attack.TargetCount > 0 ? attack.TargetCount : 1;
            for (int i = 0; i < resolvedTargets.Count && i < limit; i++) {
                IActor target = resolvedTargets[i];
                cast.Targets.TryAdd(target.ObjectId, target);
            }

            // Reuse existing pipeline to calculate and broadcast damage/effects
            actor.TargetAttack(cast);
        }
    }

    // The actors the attack's shots are aimed at, one per path: the npc's own targets first, then the nearest
    // actors of the attack's target kind the first flying path reaches (chosen here).
    private List<IActor> Aim(SkillMetadataAttack attack, IReadOnlyList<MagicPath> paths, List<IActor> attackTargets) {
        var aims = new List<IActor>(attackTargets);
        if (aims.Count >= paths.Count) {
            return aims;
        }

        MagicPath path = paths.First(NpcSplashRules.Flies);
        Vector3 from = actor.Position;
        float low = MathF.Min(0, path.ControlEndOffsetValue.Z) - AimHeight;
        var prism = new Tools.Collision.Prism(new Tools.Collision.Circle(new Vector2(from.X, from.Y), path.Distance + NpcSplashRules.AimSlack),
            from.Z + low, MathF.Abs(path.ControlEndOffsetValue.Z) + 2 * AimHeight);
        IEnumerable<IActor> pool = actor.Field.GetTargets(actor, [prism], attack.Range, int.MaxValue, aims);
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

    // The height band above and below the shot's start and end an aim is looked for in: chosen here.
    private const float AimHeight = 300f;
}
