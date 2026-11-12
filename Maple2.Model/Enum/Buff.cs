namespace Maple2.Model.Enum;

public enum BuffType {
    None = 0,
    Buff = 1,
    Debuff = 2,
    Debuff2 = 3, // Also a debuff?
}

[Flags]
public enum BuffSubType {
    None = 0,
    Buff = 1, // Used for lots of random things
    Status = 2,
    Damage = 4,
    Motion = 8,
    Recovery = 16,
    Consumable = 32, // Healing, Souvenir, ???
    PcBang = 64,
    Fishing = 128,
    Guild = 256,
    Lapenta = 512,
    Prestige = 1024,
}

public enum BuffCategory {
    None = 0,
    Unknown1 = 1,
    Unknown2 = 2,
    Unknown4 = 4,
    EnemyDot = 6,
    Stunned = 7, // ?
    Slow = 8,
    BossResistance = 9,
    Unknown99 = 99,
    MonsterStunned = 1007, // ?
    Unknown2001 = 2001,
}

public enum BuffEventType {
    None = 0,
    AutoFish = 1,
    SafeRiding = 2,
    AmphibiousRide = 3,
    AutoPerform = 4,
}

public enum BuffKeepCondition {
    TimerDuration = 0, // ?
    SkillDuration = 1, // ?
    TimerDurationTrackCooldown = 5, // ?
    UnlimitedDuration = 99,
}

public enum BuffResetCondition {
    ResetEndTick = 0,
    PersistEndTick = 1, // end tick does not reset
    Reset2 = 2, // behaves the same as Reset ??
    Replace = 3, // Removes old buff and adds a new
}

public enum BuffDotCondition {
    Default = 0, // Maybe activate on enable?
    OnInterval = 1,
    Stack = 2,
}

[Flags]
public enum BuffFlag {
    None = 0,
    UpdateBuff = 1,
    UpdateShield = 2,
}

// The invoke effect types the client counts (AdditionalCliList::CollectInvokeEffectStats, additionalcli_2.cpp:851):
// an invoke keyed on a skill (skillID, skillGroupID) counts with a skill type, one keyed on an effect (effectID,
// effectGroupID) with an effect type (specialability.cpp FUN_14081d8a0, FUN_14081d8e0); any other type the client
// drops. The client's skill types are 1001-1004; the ingest maps them to the server's numbers (PlayedClientAdditionalEffects).
public enum InvokeEffectType : byte {
    ReduceCooldown = 1, // client 1001 (rate) and 1002 (seconds)
    IncreaseSkillDamage = 2, // client 1004
    IncreaseEffectDuration = 3, // client 3 (rate) and 4 (milliseconds)
    IncreaseDotDamage = 5,
    // 20, 21 (90050324, on 10200081): not in the played client, and the client drops the types.
    IncreaseEvasionDebuff = 23,
    IncreaseCritEvasionDebuff = 26,
    // 34, 35 (90050853, 90050854, on 90050852): the client drops the types; 90050854 is not in the played client and
    // its 90050853 has no invoke.
    IncreaseOffensivePhysicalDamage = 38, // offensive_physicaldamage of the keyed effect, in thousandths
    IncreaseOffensiveMagicalDamage = 40, // offensive_magicaldamage of the keyed effect, in thousandths
    ReduceSpiritCost = 56, // client 1003
    // 57 (90050351, on skill 10500061): not in the played client, and the client drops the type.
    IncreaseHealing = 58,
}

public enum BuffCompulsionEventType : byte {
    None = 0,
    CritChanceOverride = 1,
    EvasionChanceOverride = 2,
    BlockChance = 3,
}
