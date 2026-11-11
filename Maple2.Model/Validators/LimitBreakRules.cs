namespace Maple2.Model.Validators;

// LimitBreak modes 0 and 1: u8 mode, i64 item uid. The client runs the same item check before either.
public static class LimitBreakRules {
    // The enchant grade an item must have reached before its first limit break.
    public const int EnchantMaxGrade = 15;

    // The client's check: the item has a limit break max, and an item not yet limit broken is at its enchant max.
    public static bool IsItem(int maxLevel, int level, int enchants) {
        if (maxLevel <= 0 || level < 0) {
            return false;
        }
        return level > 0 || enchants >= EnchantMaxGrade;
    }

    public static bool IsMaxLevel(int maxLevel, int level) {
        return level >= maxLevel;
    }

    // Mode 1 pays for the preview mode 0 made: it must be of this item at its present level.
    public static bool IsStaged(long stagedUid, int stagedLevel, long itemUid, int level) {
        return stagedUid == itemUid && stagedLevel == level + 1;
    }

    // Nothing is taken unless everything can be: the meso is looked at before the materials are consumed.
    public static bool CanPay(long meso, long mesoCost) {
        return mesoCost >= 0 && meso >= mesoCost;
    }
}
