namespace Maple2.Server.Game.Util;

public static class RevivalRules {
    // A revive in place (meret, coupon or meso) is refused while any buff on the player has disableRevivalHere.
    public static bool BuffBlocks(IEnumerable<bool> disableRevivalHereOfBuffs) {
        foreach (bool flag in disableRevivalHereOfBuffs) {
            if (flag) {
                return true;
            }
        }
        return false;
    }

    // Client UIRevivalDialog::RefreshMeratButton: the Lua price scaled by 1 - min(fee reduce limit, the
    // character's fee reduce rate), through ScaleValue (truncate after adding 0.000001), never below 0.
    public static long MeretCost(long luaCost, float reduceLimit, float reduceRate) {
        float rate = reduceLimit < reduceRate ? reduceLimit : reduceRate;
        long cost = (long) ((double) luaCost * (double) (1.0f - rate) + 0.000001);
        return cost < 0 ? 0 : cost;
    }

    // The meret revive is closed once the debuff it leaves is at its top stack count, unless the field
    // has infinityMeratRevival. A debuff code of 0 (no row in the constants) never closes it.
    public static bool DebuffAllows(int debuffCode, int stacks, int maxStacks, bool infiniteField) {
        if (infiniteField || debuffCode == 0) {
            return true;
        }
        return maxStacks <= 0 || stacks < maxStacks;
    }
}
