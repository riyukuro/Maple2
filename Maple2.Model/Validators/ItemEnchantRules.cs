using Maple2.Model.Metadata;

namespace Maple2.Model.Validators;

// RequestItemEnchant 0x005E, modes 1 (stage) and 4 (enchant).
public static class ItemEnchantRules {
    // The last enchant grade the server's cost, rate and option tables have, for every slot. The client reads the
    // grade limit per slot from enchantLimitTable.xml (maxGrade, 15 for every KR row).
    public const int MaxGrade = 15;

    // The client's test before it lists or stages an item (CItemEnchantLimitTable::CanEnchant,
    // itemenchantlimittable.cpp:185), in its order: gear (item type 1), no expiry (any item with a time limit,
    // expired or not: CItem::FUN_140525c60 is expiry > 0), not limit broken, a row for the gear subtype, the item's
    // required level inside the row's band, the grade below the row's maxGrade (and below the server's last grade),
    // and the item not excepted from enchanting (<limit exceptEnchant>, CItemData+0x1a1).
    public static bool CanEnchant(EnchantLimitTable limits, int type, int subType, int requiredLevel, bool enableEnchant,
                                  int enchants, int limitBreakLevel, long expiryTime) {
        if (type != 1 || expiryTime > 0 || limitBreakLevel > 0) {
            return false;
        }
        if (!limits.Slots.TryGetValue(subType, out EnchantLimitTable.Limit? limit)) {
            return false;
        }
        if (requiredLevel < limit.MinLevel || requiredLevel > limit.MaxLevel) {
            return false;
        }
        return enchants >= 0 && enchants < Math.Min(limit.MaxGrade, MaxGrade) && enableEnchant;
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

    // The Peachy experience an Ophelia try clears gives back 10% of the materials spent on it
    // (s_itemenchant_exp_payback): the tries it took at the grade's exp per try, times each material, a tenth
    // of that, rounded down.
    public const int ExpPaybackPercent = 10;

    public static int ExpPayback(int exp, int expPerTry, int amountPerTry) {
        if (exp <= 0 || expPerTry <= 0 || amountPerTry <= 0) {
            return 0;
        }
        long tries = exp / expPerTry;
        return (int) Math.Min(int.MaxValue, tries * amountPerTry * ExpPaybackPercent / 100);
    }
}
