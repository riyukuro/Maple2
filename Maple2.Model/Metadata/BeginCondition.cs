using Maple2.Model.Enum;
using Maple2.Model.Game;

namespace Maple2.Model.Metadata;

public record BeginCondition(
    short Level,
    long Mesos,
    Gender Gender,
    JobCode[] JobCode,
    float Probability,
    float CooldownTime,
    int DurationWithoutDamage,
    int DurationWithoutMoving,
    bool OnlyShadowWorld,
    bool OnlyFlyableMap,
    bool AllowDead,
    bool AllowOnBattleMount,
    bool OnlyOnBattleMount,
    bool AllowOnSurvival,
    DungeonGroupType[] DungeonGroupType,
    IReadOnlyDictionary<BasicAttribute, long> Stat,
    int[] Maps,
    MapType[] MapTypes,
    Continent[] Continents,
    int[] ActiveSkill,
    BeginConditionWeapon[]? Weapon,
    BeginConditionTarget? Target,
    BeginConditionTarget? Owner,
    BeginConditionTarget? Caster,
    // Seconds a recharge skill is not cast again after a use (defaultRechargingCooldownTime).
    float RechargeGap = 0,
    // Outside PvP zones only, in them only, or anywhere (applyPvPZoneType).
    PvpZoneApply PvpZone = PvpZoneApply.Everywhere,
    // The proc's probability is cut by the number of targets the attack hit (useTargetCountFactor): times the
    // server constant invokeEffectTargetCountFactor<n>, n the target count (SkillCastRules.TargetCountFactor).
    bool UseTargetCountFactor = false,
    // The played client's requireEffect codes: every one must be an enabled buff the caster holds (answer 0x14).
    int[]? RequireEffects = null,
    // The played client's hasNotAdditionalEffectGroup ids: an enabled buff held of one of these groups refuses the
    // cast (answer 0x18). Both are read from the skill's <basic><condition> (SkillCondition::Load) and tested by the
    // client's cast check (CSkillSystem::FUN_14176a8c0); SkillCastCheck tests them on a cast's first motion.
    int[]? NotEffectGroups = null);

public record BeginConditionWeapon(
    ItemType LeftHand,
    ItemType RightHand);

public record BeginConditionTarget(
    BeginConditionTarget.HasBuff[] Buff,
    BeginConditionTarget.EventCondition Event,
    BeginConditionTarget.BeginConditionStat[] Stat,
    ActorState[] States,
    ActorSubState[] SubStates,
    IReadOnlyDictionary<MasteryType, int> Masteries,
    //string[] NpcTags // not used?
    int[] NpcIds,
    int[] HasNotBuffIds,
    // The count of actors around the checked one (targetCheckRange, targetInRangeCount, targetFriendly,
    // targetCountSign); null when the condition has no range or no sign, as the client sets it.
    BeginConditionTarget.TargetCheck? InRange = null,
    // hasNotBuffOwner: only a HasNotBuffIds buff the condition's owner cast fails the test (SkillUtils).
    bool HasNotBuffOwned = false,
    // NpcRanks: the condition holds only for an npc whose class is in the list (SkillUtils). Chosen here, FORK.md.
    int[]? NpcClasses = null
) {
    public record HasBuff(int Id, short Level, bool Owned, int Count, CompareType Compare);

    // IgnoreSkillIds (eventIgnoreSkillID): an event from one of these skills fails the condition (SkillUtils). Null in
    // effect rows from an ingest before the field.
    public record EventCondition(EventConditionType Type, bool IgnoreOwner, int[] SkillIds, int[] BuffIds, int[]? IgnoreSkillIds = null);
    public record BeginConditionStat(BasicAttribute Attribute, float Value, CompareType Compare, CompareStatValueType ValueType);

    // Friendly 1 counts the checked actor's enemies, 2 its allies; distances are in field units (600 is 6 m).
    public record TargetCheck(int RangeDistance, int MinRangeDistance, int RangeCount, int Friendly, CompareType Compare);
}


