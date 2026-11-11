using System.Runtime.InteropServices;
using Maple2.Model.Enum;

namespace Maple2.Model.Game;

[StructLayout(LayoutKind.Sequential, Pack = 4, Size = 4)]
// The client keeps a basic option as one int (BaseOption). In an item's option groups it is always a
// flat value (CItemStatus::GetStatValue). In the enchant and limit break options it is a rate for the
// ratio stats and a flat value for the others; a rate is stored in thousandths and read back by
// GetRate as value / 1000. A table rate is rounded to the nearest thousandth: in float 0.251 * 1000
// is just below 251, so rounding down would lose one.
public readonly record struct BasicOption(int Value) {
    public BasicOption(float rate) : this((int) MathF.Round(rate * 1000f)) { }

    // CItemStatus::IsRatioStat: defense, minimum and maximum weapon attack.
    public static bool IsRatio(BasicAttribute attribute) {
        return attribute is BasicAttribute.Defense or BasicAttribute.MinWeaponAtk or BasicAttribute.MaxWeaponAtk;
    }

    public static BasicOption operator +(BasicOption self, BasicOption other) {
        return new BasicOption(self.Value + other.Value);
    }

    public static BasicOption operator -(BasicOption self, BasicOption other) {
        return new BasicOption(Math.Max(self.Value - other.Value, 0));
    }
}

[StructLayout(LayoutKind.Sequential, Pack = 4, Size = 4)]
// The client keeps a special option as one int, like a basic option (BaseOption, read as an int by
// CItemOption::Deserialize, itemoptiondata_1.cpp:252-257). A rate is stored in thousandths, rounded to
// the nearest; a value is stored as it is.
public readonly record struct SpecialOption(int Value) {
    public SpecialOption(float rate) : this((int) MathF.Round(rate * 1000f)) { }

    public static SpecialOption operator +(SpecialOption self, SpecialOption other) {
        return new SpecialOption(self.Value + other.Value);
    }

    public static SpecialOption operator -(SpecialOption self, SpecialOption other) {
        return new SpecialOption(Math.Max(self.Value - other.Value, 0));
    }
}

public readonly struct LockOption {
    private readonly BasicAttribute? basic;
    private readonly SpecialAttribute? special;
    private readonly bool lockValue;

    public LockOption(BasicAttribute attribute, bool lockValue = false) {
        basic = attribute;
        this.lockValue = lockValue;
    }

    public LockOption(SpecialAttribute attribute, bool lockValue = false) {
        special = attribute;
        this.lockValue = lockValue;
    }

    public bool TryGet(out BasicAttribute attribute, out bool valueLocked) {
        valueLocked = lockValue;
        if (basic == null) {
            attribute = 0;
            return false;
        }

        attribute = (BasicAttribute) basic;
        return true;
    }

    public bool TryGet(out SpecialAttribute attribute, out bool valueLocked) {
        valueLocked = lockValue;
        if (special == null) {
            attribute = 0;
            return false;
        }

        attribute = (SpecialAttribute) special;
        return true;
    }
}
