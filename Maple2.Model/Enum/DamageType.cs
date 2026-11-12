namespace Maple2.Model.Enum;

public enum DamageType : byte {
    Normal = 0,
    Critical = 1,
    Miss = 2,
    Block = 3,
    // The target holds a buff with the invincible flag: the hit comes with amount 0 (the client's GetGradeName calls
    // 4 "Invincible" and 8 "InvincibleReflection"). KMS2 sends 8 when the buff's invincible value is 2, 4 when it is 1.
    Invincible = 4,
    InvincibleReflection = 8,
}
