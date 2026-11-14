using Maple2.Model.Enum;

namespace Maple2.Model.Metadata;

public record AdditionalEffectMetadata(
    int Id,
    short Level,
    BeginCondition Condition,
    AdditionalEffectMetadataProperty Property,
    AdditionalEffectMetadataConsume Consume,
    AdditionalEffectMetadataReflect Reflect,
    AdditionalEffectMetadataUpdate Update,
    AdditionalEffectMetadataStatus Status,
    AdditionalEffectMetadataRecovery? Recovery,
    AdditionalEffectMetadataDot Dot,
    AdditionalEffectMetadataShield? Shield,
    AdditionalEffectMetadataInvokeEffect? InvokeEffect,
    AdditionalEffectMetadataModifyOverlapCount[] ModifyOverlapCount,
    SkillEffectMetadata[] Skills,
    SkillEffectMetadata[] TickSkills);

public record AdditionalEffectMetadataProperty(
    BuffType Type, // 1,2,3
    BuffSubType SubType, // 0,1,2,4,6,8,16,32,64,128,256,512,1024
    BuffCategory Category, // 0,1,2,4,6,7,8,9,99,1007,2001
    BuffEventType EventType, // 0,1,2,3,4
    int Group,
    int DurationTick,
    int IntervalTick,
    int DelayTick,
    int MaxCount,
    bool UseInGameTime,
    bool KeepOnDeath,
    bool RemoveOnLogout,
    bool RemoveOnLeaveField,
    bool RemoveOnPvpZone,
    bool KeepOnEnterPvpZone,
    bool CasterIndividualBuff,
    int Exp,
    int CooldownTime,
    int RideId,
    int ImmuneBreak,
    int Stun,
    BuffKeepCondition KeepCondition,
    BuffResetCondition ResetCondition,
    BuffDotCondition DotCondition,
    int ClearOnDistanceFromCaster,
    bool DisableRevivalHere = false, // BasicProperty disableRevivalHere: no revive in place while the buff is on
    int[]? SkillUseDisableIds = null, // MotionProperty skillUseDisableIDs: the skills the holder cannot cast
    CooldownGroupRate[]? CooldownGroupRates = null, // SpecialEffectProperty type 4: shorter cooldowns for a cooldown group
    int InvokeGroupId = 0, // the played client's Basic groupIDs of the first level: the group an invoke's effectGroupID names
    int SkillUseDisableMask = 0, // MotionProperty skillUseDisable: bit n set = the holder casts no skill of main type n
    bool IgnoreReduceTime = false, // BasicProperty ignoreReduceTimeCondition: the holder's duration reductions do not apply
    bool ClearCooldownOnPvpZone = false, // BasicProperty clearCooldownFromPVPZone: entering a PvP zone ends the effect's cooldown
    int SuperArmor = 0, // MotionProperty superArmor: the holder's super armor level (see SuperArmorRules)
    int SuperArmorApplyField = 0, // MotionProperty superArmorApplyFieldType: where it counts (0 anywhere, 1 off PvP maps, 2 on them)
    int ForceApplyTarget = 0, // BasicProperty forceApplyTarget: target bits the holder's skills also take (1 hostile, 2 friendly)
    AttackPossibleMetadata? AttackPossible = null, // BasicProperty attackPossible*: the only hits the holder takes
    bool PvpOnOff = false, // BasicProperty pvpOnOff: on a pk field no other player is hostile to the holder, nor the holder to them
    SpecialEffectMetadata? SpecialEffect = null, // SpecialEffectProperty of the types the server applies (SpecialEffectRules)
    bool Hide = false); // the client's Motion hide: the holder is no target of a range without applyTarget bit 4 (BuffManager.Hidden)

// BasicProperty attackPossibleSkillIDs, attackPossibleDotEffectIDs and attackPossibleEffectID (additionalactionadditional.cpp:1080):
// the skills and the damage over time effects that still hit the holder, and an effect whose holders hit it with anything.
// The client loads and compares them only; the server applies them (AttackPossibleRules).
public record AttackPossibleMetadata(int[] SkillIds, int[] DotEffectIds, int EffectId);

public record CooldownGroupRate(int GroupId, float Rate);

// A level's SpecialEffectProperty (additionalactionadditional.h:502, one per level): its type, the chance it takes
// effect (an absent probability is 1, as the client's Clear sets it) and the activateMsg string key, with the values
// of the types the server applies. The client applies only type 4 (the cooldown groups, CooldownGroupRates); the
// others are applied by the server (SpecialEffectRules). Rates are extras on top of the base amount: 1 doubles it.
public record SpecialEffectMetadata(
    int Type,
    float Probability,
    string ActivateMessage,
    float ExpRate = 0, // types 7, 9, 10 improveExpRate
    float MesoRate = 0, // type 7 improveMesoRate
    float RueRate = 0, // type 7 improveLuRate
    float MasteryRate = 0, // type 9 improveMasteryRate
    bool OnlyBigSize = false, // type 9 onlyBigSize: a prize fish only
    int RepeatType = 0, // type 7 repeatType: the quests' repeat type (2 daily)
    float RewardRate = 0, // type 11 improveRewardRate
    ExpType ExpType = ExpType.none, // type 13 expType, the client's exp kind (4 rareChest, 6 normalChest)
    int GlobalDropBoxId = 0, // type 13
    int IndividualDropBoxId = 0, // type 13
    int EnchantLevel = 0, // type 5
    float ImproveRate = 0, // type 5
    int[]? ItemIds = null, // type 8 itemIDs
    int[]? NpcRanks = null, // type 2 npcRank
    float ReduceRate = 0); // type 2

public record AdditionalEffectMetadataConsume(
    float HpRate,
    float SpRate);

public record AdditionalEffectMetadataReflect(
    float Rate,
    int EffectId,
    short EffectLevel,
    int Count,
    int PhysicalRateLimit,
    int MagicalRateLimit,
    IReadOnlyDictionary<BasicAttribute, long> Values,
    IReadOnlyDictionary<BasicAttribute, float> Rates,
    // reflectionDamageReduceRate (m_fReflectionDamageReduceRate): the part of the reflected share taken away (ReflectRecord.Share).
    float DamageReduceRate = 0);

public record AdditionalEffectMetadataUpdate(
    AdditionalEffectMetadataUpdate.CancelEffect? Cancel,
    int[] ImmuneIds,
    BuffCategory[] ImmuneCategories,
    int[] ResetCooldown,
    AdditionalEffectMetadataUpdate.ModifyDuration[] Duration,
    AdditionalEffectMetadataUpdate.RemoveRechargeUses? RemoveRecharge = null
) {
    public record CancelEffect(bool CheckSameCaster, bool PassiveEffect, int[] Ids, BuffCategory[] Categories);

    // <RemoveRechargingCount skillID count>: the start gives the skill back count recharge uses (SkillPacket.RechargeUses).
    public record RemoveRechargeUses(int SkillId, int Count);

    public record ModifyDuration(int Id, float Rate, float Value);
}

public record AdditionalEffectMetadataStatus(
    IReadOnlyDictionary<BasicAttribute, long> Values,
    IReadOnlyDictionary<BasicAttribute, float> Rates,
    IReadOnlyDictionary<SpecialAttribute, float> SpecialValues,
    IReadOnlyDictionary<SpecialAttribute, float> SpecialRates,
    IReadOnlyDictionary<BasicAttribute, float> Resistances,
    bool DeathResistanceHp,
    AdditionalEffectMetadataStatus.CompulsionEvent? Compulsion,
    AdditionalEffectMetadataStatus.StatConversion? Conversion,
    int ImmuneBreak,
    bool Invincible,
    // DefensiveProperty invincible is 2: a hit on the holder is sent as InvincibleReflection, not Invincible.
    bool InvincibleReflect = false,
    // DefensiveProperty papDamageR and mapDamageR: the share of physical and magical hits on the holder taken away
    // (0.99 is "damage taken from enemies -99 %"; a negative one adds to the hit).
    float PhysicalDamageTakenRate = 0,
    float MagicalDamageTakenRate = 0,
    // StatusProperty itemRecoveryHPRate and healRecoveryHPRate: the share added to the health the holder gets from
    // potion heals and from other heals (-0.5 is "recovery -50 %").
    float ItemRecoveryRate = 0,
    float HealRecoveryRate = 0) {

    public record CompulsionEvent(BuffCompulsionEventType Type, float Rate, int[] SkillIds);

    public record StatConversion(BasicAttribute BaseAttribute, BasicAttribute ResultAttribute, float Rate);
}

public record AdditionalEffectMetadataRecovery(
    float RecoveryRate,
    long HpValue,
    float HpRate,
    float HpConsumeRate,
    long SpValue,
    float SpRate,
    float SpConsumeRate,
    long EpValue,
    float EpRate,
    bool DisableCrit);

public record AdditionalEffectMetadataDot(
    AdditionalEffectMetadataDot.DotDamage? Damage,
    AdditionalEffectMetadataDot.DotBuff? Buff
) {
    public record DotDamage(
        AttackType Type,
        Element Element,
        bool UseGrade,
        float Rate,
        int HpValue,
        int SpValue,
        int EpValue,
        float DamageByTargetMaxHp,
        float RecoverHpByDamage,
        bool IsConstDamage, // Use 'Value' to determine damage
        bool NotKill);

    public record DotBuff(
        DotTargetType Target, // 0,1
        int Id,
        short Level);
}

public record AdditionalEffectMetadataShield(
    long HpValue,
    float HpByTargetMaxHp);

public record AdditionalEffectMetadataInvokeEffect(
    InvokeEffectType[] Types,
    float[] Values,
    float[] Rates,
    int EffectId,
    int EffectGroupId,
    int SkillId,
    int SkillGroupId);

public record AdditionalEffectMetadataModifyOverlapCount(
    int Id,
    int OffsetCount);
