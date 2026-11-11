namespace Maple2.Model.Validators;

// RequestItemEnchant 0x005E, modes 1 (stage) and 4 (enchant).
public static class ItemEnchantRules {
    // The last enchant grade. The client reads the grade limit per slot from enchantLimitTable.xml
    // (maxGrade); the server's cost, rate and option tables end at this grade for every slot.
    public const int MaxGrade = 15;

    // The client's test before it lists or stages an item (itemenchantlimittable.cpp:185): the item can be
    // enchanted at all, is not limit broken, and is below the last grade.
    public static bool CanEnchant(bool enableEnchant, int enchants, int limitBreakLevel) {
        return enableEnchant && limitBreakLevel <= 0 && enchants is >= 0 and < MaxGrade;
    }
}
