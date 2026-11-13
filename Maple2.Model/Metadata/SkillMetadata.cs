using System.Numerics;
using Maple2.Model.Enum;

namespace Maple2.Model.Metadata;

// Note: This class only exists to store metadata in db.
public record StoredSkillMetadata(
    int Id,
    string? Name,
    SkillMetadataProperty Property,
    SkillMetadataState State,
    IReadOnlyDictionary<short, SkillMetadataLevel> Levels) : ISearchResult;

public record SkillMetadata(
    int Id,
    short Level,
    string? Name,
    SkillMetadataProperty Property,
    SkillMetadataState State,
    SkillMetadataLevel Data);

public record SkillMetadataProperty(
    SkillType Type,
    SkillSubType SubType,
    RangeType RangeType,
    AttackType AttackType,
    Element Element,
    ActorState State,
    bool ContinueSkill,
    bool SpRecoverySkill,
    bool ImmediateActive,
    //bool WeaponDependency, // 1,10300179,10300184
    bool UnrideOnHit,
    bool UnrideOnUse,
    bool ReleaseObjectWeapon,
    //bool DisableWater, // 10500061,10500065,10500190 (Sharp Eyes)
    //bool HoldAttack, // 10700171,10700252
    int SkillGroup,
    short MaxLevel,
    string Emotion,
    // The played client's main type of the skill (PlayedClientSkills); 0 when it has none or no played client was given.
    int MainType = 0);

public record SkillMetadataState(
    bool InBattle,
    SuperArmor SuperArmor,
    bool UseInGameTime,
    int CooldownGroupId,
    bool IgnoreReduceCooldown,
    // The played client's count when a played client was given (PlayedClientSkills), else the data pack's.
    int RechargeMaxCount,
    // The skill counts its recharge uses on its origin skill (PlayedClientSkills).
    bool RechargeShared = false,
    // stateAttr clearCooldownFromPVPZone: entering a PvP zone ends the skill's cooldown.
    bool ClearCooldownOnPvpZone = false);

public record SkillMetadataLevel(
    BeginCondition Condition,
    SkillMetadataChange? Change,
    SkillMetadataAutoTargeting? AutoTargeting,
    SkillMetadataConsume Consume,
    SkillMetadataDetect Detect,
    SkillMetadataRecovery Recovery,
    SkillEffectMetadata[] Skills,
    SkillMetadataMotion[] Motions,
    SkillMetadataUpgrade? Upgrade = null);

// What a skill needs to be at this level in the skill tree (the level's upgrade element): the character
// level, other skills of the tree at a level, and quests. Null in metadata ingested before it was added.
public record SkillMetadataUpgrade(
    short Level,
    int[] SkillIds,
    int[] SkillLevels,
    int[] QuestIds);

public record SkillMetadataChange(
    SkillMetadataChange.Skill Origin,
    SkillMetadataChange.Effect[] Effects,
    SkillMetadataChange.Skill[] Skills) {

    public record Skill(int Id, int Level);

    public record Effect(int Id, int Level, int OverlapCount);
}

public record SkillMetadataAutoTargeting(
    float MaxDegree,
    float MaxDistance,
    float MaxHeight,
    bool UseMove);

public record SkillMetadataConsume(
    long Meso,
    bool UseItem,
    float HpRate,
    Dictionary<BasicAttribute, long> Stat);

public record SkillMetadataDetect(
    bool IncludeCaster, // range & arrow can use 2, but detect & sensor only use 0 & 1
    float Distance);

public record SkillMetadataRecovery(
    long SpValue,
    float SpRate);

public record SkillMetadataMotion(
    SkillMetadataMotionProperty MotionProperty,
    SkillMetadataAttack[] Attacks,
    IReadOnlyDictionary<string, byte> AttackPoints);

public record SkillMetadataMotionProperty(
    string SequenceName,
    float SequenceSpeed,
    float MoveDistance,
    bool FaceTarget,
    int MoveType = 0);

public record SkillMetadataAttack(
    string Point,
    int PointGroup,
    int TargetCount,
    long MagicPathId,
    long CubeMagicPathId,
    int HitImmuneBreak, // 0, 1, 2, 5, 99
    int BrokenOffence, // 0 - 99
    CompulsionType[] CompulsionTypes,
    SkillMetadataPet? Pet,
    SkillMetadataRange Range,
    SkillMetadataArrow Arrow,
    SkillMetadataDamage Damage,
    SkillEffectMetadata[] Skills,
    SkillEffectMetadata[] SkillsOnDamage);

public record SkillMetadataPet(
    int TamingGroup,
    int TrapLevel,
    int TamingPoint,
    bool ForcedTaming);

public record SkillMetadataRange(
    SkillRegion Type,
    float Distance,
    float Height,
    float Width,
    float EndWidth,
    float RotateZDegree,
    Vector3 RangeAdd,
    Vector3 RangeOffset,
    IncludeCasterType IncludeCaster,
    ApplyTargetType ApplyTarget, // 0,1,2,3,5,6,7,8
    SkillTargetType CastTarget, // 0,1,2,3,4,5,7
    SkillMetadataRangeSelect? Select = null);

// The range's target choice (rangeProperty targetSelectType ... targetStatCompareFunction). Mode 2 keeps only the
// targets that pass (none if none pass), mode 3 keeps them if any pass, other modes keep all. A target passes when it
// holds the enabled buff BuffId (0: any; from the caster if BuffFromCaster) and, between two players, shares the
// caster's party (SameParty) and guild (SameGuild). StatSort orders the targets by Stat: 1 current ascending,
// 2 descending, 3 current/total ascending, 4 descending; 0 leaves the order.
public record SkillMetadataRangeSelect(
    int Mode,
    int BuffId,
    bool BuffFromCaster,
    bool SameParty,
    bool SameGuild,
    BasicAttribute Stat,
    int StatSort);

public record SkillMetadataArrow(
    bool Overlap,
    bool Explosion,
    bool RayPhysXTest,
    SkillTargetType NonTarget,
    BounceType BounceType,
    int BounceCount,
    float BounceRadius,
    bool BounceOverlap,
    Vector3 Collision,
    Vector3 CollisionAdd,
    int RayType);

public record SkillMetadataDamage(
    int Count,
    float Rate,
    float HitSpeed,
    float HitDelay,
    bool IsConstDamage, // Use 'Value' to determine damage
    long Value,
    float DamageByTargetMaxHp,
    bool SuperArmorBreak,
    SkillMetadataPush? Push);

public record SkillMetadataPush(
    int Type, // 0, 1, 2, 3, 4, 5, -1
    int EaseType, // 0, 1, 2
    int ApplyField, // 0, 1, 2
    float Distance,
    float UpDistance,
    int Down,
    bool Fall,
    float Duration,
    float Probability,
    int Priority, // 0-99
    int PriorityHitImmune); // 1-1600
