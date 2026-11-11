namespace Maple2.Model.Validators;

public static class LapenshardRules {
    // Lapenshard 5 (upgrade): each material entry is an item uid and an amount. The amount is at least 1 and
    // at most what the stack holds. The stack of the shard being upgraded may be a material only for what it
    // holds beyond the one shard that is upgraded.
    public static bool IsMaterialAmount(int amount, int owned, bool isUpgradedStack) {
        if (amount < 1) {
            return false;
        }

        return amount <= (isUpgradedStack ? owned - 1 : owned);
    }
}
