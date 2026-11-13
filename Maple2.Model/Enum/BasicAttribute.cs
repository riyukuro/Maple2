namespace Maple2.Model.Enum;

public enum BasicAttribute : byte {
    Strength = 0,
    Dexterity = 1,
    Intelligence = 2,
    Luck = 3,
    Health = 4,
    HpRegen = 5,
    HpRegenInterval = 6,
    Spirit = 7,
    SpRegen = 8,
    SpRegenInterval = 9,
    Stamina = 10,
    StaminaRegen = 11,
    StaminaRegenInterval = 12,
    AttackSpeed = 13,
    MovementSpeed = 14,
    Accuracy = 15,
    Evasion = 16,
    CriticalRate = 17,
    CriticalDamage = 18,
    CriticalEvasion = 19,
    Defense = 20,
    PerfectGuard = 21,
    JumpHeight = 22,
    PhysicalAtk = 23,
    MagicalAtk = 24,
    PhysicalRes = 25,
    MagicalRes = 26,
    MinWeaponAtk = 27,
    MaxWeaponAtk = 28,
    Damage = 29,
    Unknown = 30, // "Damage"
    Piercing = 31,
    MountSpeed = 32,
    BonusAtk = 33,
    PetBonusAtk = 34,
}

// The client's basic stat id of a server stat. The client's ids (table/statstringtable.xml) end 27 wapmin, 28 wapmax,
// 29 bap, 30 bap_pet, 31 pen, 32 rmsp, with no row for 33 and 34; the server's enum follows the data pack's StatValue
// order (wapmin, wapmax, dmg, ?, pen, rmsp, bap, bap_pet), so its BonusAtk and PetBonusAtk (33, 34) are the client's
// 29 and 30, and its Damage and Unknown (29, 30) take the client's unnamed 33 and 34 (Chosen here: no client reader
// names them). Every packet field that carries a basic stat id goes through these; storage and ingest keep the
// server's numbers. The character panel's Bonus Attack row reads stat 0x1d (UIPCInfoDialog::vfunc_86,
// uipcinfodialog.cpp:965).
public static class BasicAttributeIds {
    public static byte ClientStat(this BasicAttribute attribute) => attribute switch {
        BasicAttribute.BonusAtk => 29,
        BasicAttribute.PetBonusAtk => 30,
        BasicAttribute.Damage => 33,
        BasicAttribute.Unknown => 34,
        _ => (byte) attribute,
    };

    // The server stat of a client stat id (the inverse of ClientStat).
    public static BasicAttribute ServerStat(int id) => id switch {
        29 => BasicAttribute.BonusAtk,
        30 => BasicAttribute.PetBonusAtk,
        33 => BasicAttribute.Damage,
        34 => BasicAttribute.Unknown,
        _ => (BasicAttribute) id,
    };
}
