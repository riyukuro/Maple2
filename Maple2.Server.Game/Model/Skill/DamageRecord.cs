using System.Collections.Concurrent;
using System.Numerics;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Game.Model.Skill;

public class DamageRecord {
    public readonly SkillMetadata SkillMetadata;
    public readonly SkillMetadataAttack AttackMetadata;
    public readonly DamagePropertyRecord Properties;
    public long SkillUid { get; init; }
    public long TargetUid { get; init; } // For Non-Region skills
    public int CasterId { get; init; }

    public int OwnerId;
    public int SkillId;
    public short Level;
    public byte MotionPoint;
    public byte AttackPoint;

    public Vector3 Position;
    public Vector3 Direction;

    // A region's hit (FieldSkill.Fire): the point and direction its packet names for a target at a position. The
    // target's record takes them (DamageRecordTarget.For), so the packet and the npc's push read the same values.
    // Null for a cast's hit.
    public Func<Vector3, (Vector3 Position, Vector3 Direction)>? RegionFire;

    public readonly ConcurrentDictionary<int, DamageRecordTarget> Targets; // Target's ObjectId as key

    public DamageRecord(SkillMetadata skillMetadata, SkillMetadataAttack attackMetadata) {
        SkillMetadata = skillMetadata;
        AttackMetadata = attackMetadata;
        Targets = [];
        Properties = new DamagePropertyRecord {
            Element = skillMetadata.Property.Element,
            SkillId = skillMetadata.Id,
            SkillGroup = skillMetadata.Property.SkillGroup,
            RangeType = skillMetadata.Property.RangeType,
            AttackType = skillMetadata.Property.AttackType,
            CompulsionTypes = attackMetadata.CompulsionTypes,
            Rate = attackMetadata.Damage.Rate,
            Value = attackMetadata.Damage.Value,
            SuperArmorBreak = attackMetadata.Damage.SuperArmorBreak,
        };
    }
}

public class DamageRecordTarget {
    public readonly IActor Target;
    public int ObjectId => Target.ObjectId;
    public Vector3 Position;
    public Vector3 Direction;

    private readonly List<(DamageType, long)> damage;
    public IReadOnlyList<(DamageType Type, long Amount)> Damage => damage;

    public DamageRecordTarget(IActor target) {
        Target = target;
        damage = [];
    }

    // The record of a target the damage reaches: the position and direction the damage packet sends for it. A
    // region's hit takes its fire point and the direction from there to the target (DamageRecord.RegionFire). A
    // cast's hit takes the caster's position; its direction is the caster's facing, the one a type 5 push follows
    // (NpcControlRules.PushFiveDirection), or for another push the way from the caster to the target.
    public static DamageRecordTarget For(IActor target, IActor caster, DamageRecord damage, SkillMetadataAttack attack) {
        var record = new DamageRecordTarget(target);
        if (damage.RegionFire != null) {
            (record.Position, record.Direction) = damage.RegionFire(target.Position);
            return record;
        }

        // A trigger's caster is at the map origin: the damage's own position stands in for it.
        Vector3 sourcePosition = caster.Position;
        if (sourcePosition.LengthSquared() < 0.001f && damage.Position.LengthSquared() > 0.001f) {
            sourcePosition = damage.Position;
        }
        record.Position = sourcePosition;
        record.Direction = caster.Transform.FrontAxis;

        if (attack.Damage.Push is { Type: 5 }) {
            record.Direction = NpcControlRules.PushFiveDirection(false, attack.AttackMethodType, damage.Direction, caster.Transform.FrontAxis);
        } else if (attack.Damage.Push != null) {
            // Direction should point from source toward the target (the push direction).
            Vector3 offset = target.Position - sourcePosition;
            if (offset.LengthSquared() > 0.001f) {
                record.Direction = Vector3.Normalize(offset);
            } else if (damage.Direction.LengthSquared() > 0.001f) {
                record.Direction = Vector3.Normalize(damage.Direction);
            }
        }
        return record;
    }

    public void AddDamage(DamageType type, long amount) {
        damage.Add((type, amount));
    }
}
