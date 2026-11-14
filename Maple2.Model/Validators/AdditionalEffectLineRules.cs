using Maple2.Model.Enum;

namespace Maple2.Model.Validators;

// The item lines that give an additional effect while held (specialabilitystringtable 117 additionaleffect_95000012,
// 118 additionaleffect_95000014, valueType 1: a value). The client names them in tooltips and has no other reader,
// so the server gives the effect (FORK.md). The value is the effect's level: KR rolls 1 to 10 (itemoptionvariation),
// and both effects have 20 levels. Each is a passive (keepCondition 99) that casts a 3 s debuff on the target an
// attack hits (eventCondition 6): 95000013 defense down (nddrate -0.2 % a level), 95000015 attack down (paprate and
// maprate -0.3 % a level). KR rolls the two only on ItemOptionRandom 11200069. The other additionaleffect lines (116,
// 119-128, 155) are on no KR item, so they stay unread.
public static class AdditionalEffectLineRules {
    public static readonly IReadOnlyList<(SpecialAttribute Line, int EffectId)> Lines = [
        (SpecialAttribute.AdditionalEffect95000012, 95000012),
        (SpecialAttribute.AdditionalEffect95000014, 95000014),
    ];

    // The level a line's total gives, or 0 for none: the highest level the effect has at most.
    public static short Level(long total, Func<short, bool> hasLevel) {
        for (long level = Math.Min(total, short.MaxValue); level > 0; level--) {
            if (hasLevel((short) level)) {
                return (short) level;
            }
        }
        return 0;
    }
}
