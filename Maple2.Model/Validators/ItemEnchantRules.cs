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

    // Mesos per Crystal Fragment the player is short of. The client's ingredient list
    // (uiitemenchantdialog.cpp:1617) shows the missing fragments as a meso cost, s_itemenchant_fee.
    public const long CrystalFragmentFee = 1000;

    // The client's fee for the Crystal Fragment line: (need - have) x 1000 when short, else nothing.
    public static long CrystalFragmentShortfallFee(int need, int have) {
        return need > have ? (long) (need - have) * CrystalFragmentFee : 0;
    }

    // The fail charges an Ophelia failure adds: the table's gain, up to the KR stock limit
    // (constants.xml EnchantFailStackTakeMaxCount, 1000). A limit of 0 (no constants row) caps nothing.
    public static int FailChargeGain(int held, int gain, int takeMax) {
        if (takeMax <= 0) {
            return Math.Max(0, gain);
        }
        return Math.Clamp(takeMax - held, 0, Math.Max(0, gain));
    }

    // Whether an enchant to targetLevel asks for copies of the item (equip ingredients): KR constants.xml opens
    // them at EnchantEquipIngredientOpenLevel 11 for items of EnchantEquipIngredientOpenRank 4 and above. A limit
    // of 0 (no constants row) opens them at every level and rarity; the grade table decides then.
    public static bool FodderOpen(int targetLevel, int rarity, int openLevel, int openRank) {
        return targetLevel >= openLevel && rarity >= openRank;
    }
}
