using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model.Skill;
using Maple2.Tools.VectorMath;
using Maple2.Server.Game.Packets;
using Maple2.Tools.Collision;
using Serilog;
using Maple2.Database.Storage;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Model.ActorStateComponent;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Game.Model;

/// <summary>
/// Actor is an entity that can engage in combat.
/// </summary>
/// <typeparam name="T">The type contained by this object</typeparam>
public abstract class Actor<T> : IActor<T>, IDisposable {

    protected readonly ILogger Logger = Log.ForContext<T>();
    public NpcMetadataStorage NpcMetadata { get; init; }

    public FieldManager Field { get; }
    public T Value { get; }

    public virtual StatsManager Stats { get; }

    protected readonly ConcurrentDictionary<int, DamageRecordTarget> DamageDealers = new();
    // The damage taken so far from every caster (trigger condition npc_damage).
    public long DamageTaken => DamageDealers.Values.Sum(record => record.Damage.Sum(entry => entry.Amount));

    public int ObjectId { get; }
    public virtual Vector3 Position { get => Transform.Position; set => Transform.Position = value; }
    public virtual Vector3 Rotation {
        get => Transform.RotationAnglesDegrees;
        set => Transform.RotationAnglesDegrees = value;
    }
    public Transform Transform { get; init; }
    public AnimationManager Animation { get; init; }
    public SkillState SkillState { get; init; }

    public virtual bool IsDead { get; protected set; }
    public abstract IPrism Shape { get; }
    public SkillQueue ActiveSkills { get; init; }

    public virtual BuffManager Buffs { get; }

    /// <summary>
    /// Counter for skill casting ID creation
    /// </summary>
    private int localIdCounter = 1;
    protected int NextLocalId() => Interlocked.Increment(ref localIdCounter);

    /// <summary>
    /// Tick duration of actor in the same position.
    /// </summary>
    public (Vector3 Position, long LastTick, long Duration) PositionTick { get; set; }

    protected Actor(FieldManager field, int objectId, T value, NpcMetadataStorage npcMetadata) {
        Field = field;
        ObjectId = objectId;
        Value = value;
        Buffs = new BuffManager(this);
        Transform = new Transform();
        NpcMetadata = npcMetadata;
        Animation = new AnimationManager(this);
        SkillState = new SkillState(this);
        Stats = new StatsManager(this);
        PositionTick = new ValueTuple<Vector3, long, long>(Vector3.Zero, 0, 0);
        ActiveSkills = new SkillQueue();
    }

    public void Dispose() {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing) { }

    public virtual void ApplyEffect(IActor caster, IActor owner, SkillEffectMetadata effect, long startTick, EventConditionType type = EventConditionType.Activate, int skillId = 0, int buffId = 0, bool notifyField = true) {
        Debug.Assert(effect.Condition != null);

        if (effect.Condition?.RandomCast == true) {
            int index = Random.Shared.Next(effect.Skills.Length);
            owner.Buffs.AddBuff(caster, owner, effect.Skills[index].Id, effect.Skills[index].Level, startTick, stacks: effect.Condition.OverlapCount, notifyField: notifyField, type: type);
        } else {
            foreach (SkillEffectMetadata.Skill skill in effect.Skills) {
                owner.Buffs.AddBuff(caster, owner, skill.Id, skill.Level, startTick, stacks: effect.Condition!.OverlapCount, notifyField: notifyField, type: type);
            }
        }
    }

    public virtual void ApplyDamage(IActor caster, DamageRecord damage, SkillMetadataAttack attack) {
        // Determine the source position.
        // For region/trigger skills, the caster is FieldActor at origin - use damage.Position (the skill region's position) instead.
        Vector3 sourcePosition = caster.Position;
        if (sourcePosition.LengthSquared() < 0.001f && damage.Position.LengthSquared() > 0.001f) {
            sourcePosition = damage.Position;
        }

        var targetRecord = new DamageRecordTarget(this) {
            Position = sourcePosition,
            Direction = caster.Transform.FrontAxis,
        };

        if (attack.Damage.Push != null) {
            // Direction should point from source toward the target (the push direction).
            Vector3 offset = Position - sourcePosition;
            if (offset.LengthSquared() > 0.001f) {
                targetRecord.Direction = Vector3.Normalize(offset);
            } else if (damage.Direction.LengthSquared() > 0.001f) {
                targetRecord.Direction = Vector3.Normalize(damage.Direction);
            }
        }
        damage.Targets.TryAdd(ObjectId, targetRecord);

        if (attack.Damage.Count <= 0) {
            return;
        }

        long damageAmount = 0;
        // An attackPossible limit of a buff held refuses the hit: sent as an invincible hit (AttackPossibleRules).
        bool refused = Buffs.AttackRefused(caster, damage.SkillId);
        for (int i = 0; i < attack.Damage.Count; i++) {
            Reflect(caster);
            if (caster is FieldPlayer { DeathPower: true } && this is FieldNpc { Value.Metadata.Basic.Friendly: 0 } and not FieldPet) {
                // GM toggle: the hit takes all the health that is left. Monsters only: friendly NPCs and
                // pets take normal damage.
                long health = Math.Max(1, Stats.Values[BasicAttribute.Health].Current);
                targetRecord.AddDamage(DamageType.Normal, health);
                damageAmount -= health;
            } else if (Buffs.Invincible || refused) {
                // KMS2 sends each hit on a holder of an invincible buff as type 4 or 8 (by the buff) with amount 0.
                targetRecord.AddDamage(Buffs.Invincible ? Buffs.InvincibleHitType : DamageType.Invincible, 0);
            } else if (attack.Damage.DamageByTargetMaxHp > 0) {
                long share = DamageCalculator.TargetMaxHpDamage(caster, this, attack.Damage.DamageByTargetMaxHp);
                targetRecord.AddDamage(DamageType.Normal, share);
                damageAmount -= share;
            } else if (attack.Damage.IsConstDamage) {
                targetRecord.AddDamage(DamageType.Normal, attack.Damage.Value);
                damageAmount -= attack.Damage.Value;
            } else {
                try {
                    (DamageType damageTypeResult, double damageResult) = DamageCalculator.CalculateDamage(caster, this, damage.Properties);
                    targetRecord.AddDamage(damageTypeResult, (long) damageResult);
                    damageAmount -= (long) damageResult;
                } catch (Exception e) {
                    Logger.Error(e, "Error calculating damage for {Caster} on {Target} with attack {Attack}", caster, this, attack);
                    damageAmount = 0; // No damage dealt
                }
            }
        }

        if (damageAmount != 0) {
            long positiveDamage = damageAmount * -1;
            if (!DamageDealers.TryGetValue(caster.ObjectId, out DamageRecordTarget? record)) {
                record = new DamageRecordTarget(this);
                DamageDealers.TryAdd(caster.ObjectId, record);
            }
            record.AddDamage(DamageType.Normal, positiveDamage);
            Stats.Values[BasicAttribute.Health].Add(NpcTargetRules.HealthChange(Stats.Values[BasicAttribute.Health].Current, damageAmount, this is FieldNpc { NoDie: true } || (Buffs.DeathResistance && caster is not FieldPlayer { DeathPower: true })));
            Field.Broadcast(StatsPacket.Update(this, BasicAttribute.Health));
            OnDamageReceived(caster, positiveDamage);
        }

        foreach ((DamageType damageType, long amount) in targetRecord.Damage) {
            switch (damageType) {
                case DamageType.Critical:
                    caster.Buffs.TriggerEvent(caster, caster, this, EventConditionType.OnOwnerAttackHit, skillId: damage.SkillId);
                    caster.Buffs.TriggerEvent(caster, caster, this, EventConditionType.OnOwnerAttackCrit, skillId: damage.SkillId);
                    Buffs.TriggerEvent(this, this, this, EventConditionType.OnAttacked, skillId: damage.SkillId);
                    break;
                case DamageType.Normal:
                    caster.Buffs.TriggerEvent(caster, caster, this, EventConditionType.OnOwnerAttackHit, skillId: damage.SkillId);
                    break;
                case DamageType.Block:
                    Buffs.TriggerEvent(this, this, this, EventConditionType.OnBlock, skillId: damage.SkillId);
                    break;
                case DamageType.Miss:
                    caster.Buffs.TriggerEvent(caster, caster, this, EventConditionType.OnAttackMiss, skillId: damage.SkillId);
                    Buffs.TriggerEvent(this, this, this, EventConditionType.OnEvade, skillId: damage.SkillId);
                    break;
            }
        }

        // A player hit (not evaded) by a skill: the DamageBySkill missions of a dungeon room.
        if (this is FieldPlayer player && Field is DungeonFieldManager dungeonField && targetRecord.Damage.Any(hit => hit.Type != DamageType.Miss)) {
            dungeonField.CountMissions(player, mission => mission.AddSkillHit(damage.SkillId, damage.Level));
        }
    }

    /// <summary>
    /// Credits damage that did not come from a skill hit (e.g. a GM kill) to the caster, so death handling
    /// (drops, exp, quest conditions) sees the caster as a damage dealer.
    /// </summary>
    public void RecordDamage(IActor caster, long amount) {
        DamageDealers.GetOrAdd(caster.ObjectId, _ => new DamageRecordTarget(this)).AddDamage(DamageType.Normal, amount);
        OnDamageReceived(caster, amount);
    }

    protected virtual void OnDamageReceived(IActor caster, long amount) { }

    public virtual void Reflect(IActor target) {
        if (Buffs.Reflect == null || Buffs.Reflect.Counter >= Buffs.Reflect.Metadata.Count) {
            return;
        }
        ReflectRecord record = Buffs.Reflect;

        if (record.Metadata.Rate is not 1 && record.Metadata.Rate < Random.Shared.NextDouble()) {
            return;
        }

        record.Counter++;
        if (record.Counter >= record.Metadata.Count) {
            Buffs.Remove(record.SourceBuffId, ObjectId);
        }
        target.Buffs.AddBuff(this, target, record.Metadata.EffectId, record.Metadata.EffectLevel, Field.FieldTick);

        // TODO: Reflect should also amend the target's damage record from Reflect.ReflectValues and ReflectRates
    }

    public virtual void TargetAttack(SkillRecord record) {
        if (record.Targets.Count == 0) {
            return;
        }

        // For NPC casts, ImpactPosition is never set (only comes from client packets), so fall back to caster position
        Vector3 impactPosition = record.ImpactPosition.LengthSquared() > 0.001f ? record.ImpactPosition : Position;
        var damage = new DamageRecord(record.Metadata, record.Attack) {
            CasterId = record.Caster.ObjectId,
            TargetUid = record.TargetUid,
            OwnerId = record.Caster.ObjectId,
            SkillId = record.SkillId,
            Level = record.Level,
            AttackPoint = record.AttackPoint,
            MotionPoint = record.MotionPoint,
            Position = impactPosition,
            Direction = record.Direction,
        };

        SkillEffectMetadata[] splashEffects = record.Attack.Skills.Where(e => e.Splash != null).ToArray();

        foreach (IActor target in record.Targets.Values) {
            target.ApplyDamage(this, damage, record.Attack);
        }

        Field.Broadcast(SkillDamagePacket.Damage(damage));

        ApplyEffects(record.Attack.Skills, record.Caster, this, skillId: record.SkillId, targets: record.Targets.Values.ToArray());
        ApplyEffects(record.Attack.SkillsOnDamage, record.Caster, damage, record.Targets.Values.ToArray());

        // The hit fires the caster's regions of the skills the attack names that fire only when made to
        // (splashActiveSkillID: the priest heal 10400071 fires the regions of 10400292 its caster placed).
        if (record.Attack.SplashActiveSkillIds is { Length: > 0 } splashActive) {
            Field.InvokeSplashes(record.Caster, splashActive);
        }

        // Create splash skills at target positions.
        // Always skip the caster: when IncludeCaster is set, the attack also has a CubeMagicPathId
        // which handles splash placement independently — this loop must not create a duplicate.
        // A shot that landed its own regions (SplashPlaced) leaves only the splashes that need a target here.
        splashEffects = NpcSplashRules.AtTargets(splashEffects, NpcSplashRules.NoTarget, record.SplashPlaced).ToArray();
        foreach (IActor target in record.Targets.Values) {
            if (target.ObjectId == record.Caster.ObjectId) {
                if (splashEffects.Length > 0 && record.Attack.CubeMagicPathId == 0) {
                    Logger.Warning("[TargetAttack] SkillId={SkillId} AttackPoint={AttackPoint} IncludeCaster={IncludeCaster} — caster skipped in splash loop but CubeMagicPathId=0. Splash may be lost.",
                        record.SkillId, record.AttackPoint, record.Attack.Range.IncludeCaster);
                }
                continue;
            }

            foreach (SkillEffectMetadata effect in splashEffects) {
                Field.AddSkill(record.Caster, effect, [target.Position], record.Caster.Rotation);
            }
        }
    }

    public virtual void SkillAttackPoint(SkillRecord record, byte attackPoint) {

    }

    public virtual IActor GetTarget(SkillTargetType targetType, IActor caster, IActor target, IActor owner) {
        return targetType switch {
            SkillTargetType.Owner => owner,
            SkillTargetType.Target => target,
            SkillTargetType.Caster => caster,
            SkillTargetType.None => this, // Should be on self/inherit
            SkillTargetType.Attacker => target,
            _ => throw new NotImplementedException(),
        };
    }

    public IActor GetOwner(SkillTargetType targetType, IActor caster, IActor target, IActor owner) {
        return targetType switch {
            SkillTargetType.Owner => owner,
            SkillTargetType.Target => owner,
            SkillTargetType.Caster => caster,
            SkillTargetType.None => this, // Should be on self/inherit
            SkillTargetType.Attacker => target,
            _ => throw new NotImplementedException(),
        };
    }

    /// <summary>
    /// Filter effects before applying. This is needed instead of applying as iterating to ensure the current state of the actor is used.
    /// </summary>
    // ownedBy: whose copies a hasBuffOwner test takes (the caster when null); a buff's event skills pass the buff's caster.
    public virtual void ApplyEffects(SkillEffectMetadata[] effects, IActor caster, IActor owner, EventConditionType type = EventConditionType.Activate, int skillId = 0, int buffId = 0, IActor? ownedBy = null, params IActor[] targets) {
        var appliedEffects = new List<(IActor Owner, IActor Caster, SkillEffectMetadata Effect)>();
        long startTick = Field.FieldTick;

        foreach (SkillEffectMetadata effect in effects) {
            if (effect.Condition != null) {
                foreach (IActor target in targets) {
                    IActor resultOwner = GetTarget(effect.Condition!.Target, caster, target, owner);
                    IActor resultCaster = GetOwner(effect.Condition.Owner, caster, target, owner);
                    if (effect.Condition.Condition.Check(resultCaster, resultOwner, target, type, skillId, buffId, ownedBy)) {
                        appliedEffects.Add((resultOwner, resultCaster, effect));
                    }
                }
            }
        }

        foreach ((IActor effectOwner, IActor effectCaster, SkillEffectMetadata effect) in appliedEffects) {
            ApplyEffect(effectCaster, effectOwner, effect, startTick, type, skillId, buffId);
        }
    }

    /// <summary>
    /// Used for On damage
    /// </summary>
    /// <param name="effects"></param>
    /// <param name="caster"></param>
    /// <param name="record"></param>
    /// <param name="targets"></param>
    public virtual void ApplyEffects(SkillEffectMetadata[] effects, IActor caster, DamageRecord record, params IActor[] targets) {
        // Skill does no damage, apply effects regardless
        if (record.AttackMetadata.Damage.Count == 0) {
            ApplyEffects(effects, caster, caster, skillId: record.SkillId, targets: targets);
            return;
        }

        var appliedEffects = new List<(IActor Owner, IActor Caster, SkillEffectMetadata Effect)>();
        long startTick = Field.FieldTick;
        foreach (SkillEffectMetadata effect in effects) {
            if (effect.Condition != null) {
                foreach (IActor target in targets) {
                    IActor resultOwner = GetTarget(effect.Condition!.Target, caster, target, caster);
                    IActor resultCaster = GetOwner(effect.Condition.Owner, caster, target, caster);
                    if (effect.Condition.Condition.Check(resultCaster, resultOwner, target, eventSkillId: record.SkillId) &&
                        DealtDamage(target)) {
                        appliedEffects.Add((resultOwner, resultCaster, effect));
                    }
                }
            }
        }

        foreach ((IActor effectOwner, IActor effectCaster, SkillEffectMetadata effect) in appliedEffects) {
            ApplyEffect(effectCaster, effectOwner, effect, startTick, skillId: record.SkillId);
        }
        return;

        bool DealtDamage(IActor target) {
            return record.Targets.TryGetValue(target.ObjectId, out DamageRecordTarget? targetRecord) && targetRecord.Damage.Any(x => x.Amount > 0);
        }
    }

    public virtual void Update(long tickCount) {
        if (IsDead) return;

        if (Stats.Values[BasicAttribute.Health].Current <= 0) {
            IsDead = true;
            OnDeath();
            return;
        }

        if (PositionTick.Position != Position) {
            PositionTick = new ValueTuple<Vector3, long, long>(Position, tickCount, 0);
        } else {
            PositionTick = new ValueTuple<Vector3, long, long>(Position, PositionTick.LastTick, tickCount - PositionTick.LastTick);
        }

        Animation.Update(tickCount);
        Buffs.Update(tickCount);
    }

    public virtual void KeyframeEvent(string keyName) { }

    public virtual SkillRecord? CastSkill(int id, short level, long uid, int castTick, in Vector3 position = default, in Vector3 direction = default, in Vector3 rotation = default, float rotateZ = 0f, byte motionPoint = 0) {
        SkillRecord? record = NewCast(id, level, uid, castTick, position, direction, rotation, rotateZ, motionPoint);
        if (record != null) {
            Field.Broadcast(SkillPacket.Use(record));
        }
        return record;
    }

    protected SkillRecord? NewCast(int id, short level, long uid, int castTick, in Vector3 position, in Vector3 direction, in Vector3 rotation, float rotateZ, byte motionPoint) {
        if (!Field.SkillMetadata.TryGet(id, level, out SkillMetadata? metadata)) {
            Logger.Error("Invalid skill use: {SkillId},{Level}", id, level);
            return null;
        }

        var record = new SkillRecord(metadata, uid, this) {
            Position = position == default ? Position : position,
            Rotation = SkillCastRules.CastRotation(rotation, Rotation),
            Rotate2Z = rotateZ,
            ServerTick = castTick,
        };

        if (!record.TrySetMotionPoint(motionPoint)) {
            Logger.Error("Invalid MotionPoint({MotionPoint}) for {Record}", motionPoint, record);
            return null;
        }

        return record;
    }

    protected virtual void OnDeath() {
        Buffs.TriggerEvent(this, this, this, EventConditionType.OnDeath);
    }

    /// <summary>
    /// Consumes needed stats to cast skill.
    /// </summary>
    public virtual bool SkillCastConsume(SkillRecord record) {
        return true;
    }
}
