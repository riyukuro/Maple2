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

    // The meso price of one crystal fragment the player lacks (the client's fee text,
    // uiunlimitedenchantdialog.cpp:263: (needed - owned) * 1000 for the ingredient with tag 0x60).
    public const long FragmentPrice = 1000;

    // The fee as the newest KMS2 build names it: crystal fragments, mesoCost / 1000 of them.
    public static int Fragments(long mesoCost) {
        return (int) Math.Clamp(mesoCost / FragmentPrice, 0, int.MaxValue);
    }

    // What is taken for the fee: the fragments the player owns, and meso for each one that lacks.
    public static (int Fragments, long Meso) Fee(long mesoCost, long ownedFragments) {
        int needed = Fragments(mesoCost);
        int fragments = (int) Math.Clamp(ownedFragments, 0, needed);
        return (fragments, (needed - fragments) * FragmentPrice);
    }

    // Nothing is taken unless everything can be: the meso is looked at before the materials are consumed.
    public static bool CanPay(long meso, long mesoCost) {
        return mesoCost >= 0 && meso >= mesoCost;
    }
}
