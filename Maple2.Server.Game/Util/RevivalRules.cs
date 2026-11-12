namespace Maple2.Server.Game.Util;

public enum MesoReviveRefusal {
    None,
    NotDead,
    NoRevivalHere,
    Buff,
    NoVoucher,
    DailyMax,
    LackMeso,
}

public static class RevivalRules {
    // The revive in place for mesos or a voucher (Revival mode 2). Every test comes before anything is
    // taken: a request from a living player used to take the mesos or the voucher and raise the daily count.
    // A voucher is not held to the daily count; mesos are.
    public static MesoReviveRefusal MesoRevive(bool dead, bool noRevivalHere, bool buffBlocks, bool useVoucher,
            bool hasVoucher, int usedToday, int dailyMax, long meso, long cost) {
        if (!dead) {
            return MesoReviveRefusal.NotDead;
        }
        if (noRevivalHere) {
            return MesoReviveRefusal.NoRevivalHere;
        }
        if (buffBlocks) {
            return MesoReviveRefusal.Buff;
        }
        if (useVoucher) {
            return hasVoucher ? MesoReviveRefusal.None : MesoReviveRefusal.NoVoucher;
        }
        if (usedToday >= dailyMax) {
            return MesoReviveRefusal.DailyMax;
        }
        return meso < cost || cost < 0 ? MesoReviveRefusal.LackMeso : MesoReviveRefusal.None;
    }

    // A revive that stays in the field gives the lasting buffs back itself; one that sends the player to the
    // map's revivalReturnId gets them from the field entry there.
    public static bool RestoresLastingBuffs(bool instant, int revivalReturnId, int mapId) {
        return instant || revivalReturnId == 0 || revivalReturnId == mapId;
    }

    // A player who leaves a field dead (a GM warp, a trigger move) gets a new, living actor in the next field:
    // without a fill it arrives at 0 HP, still drawn dead with no tombstone the revival dialog could use.
    public static bool FillsHpOnFieldEntry(bool leftDead, long currentHp) {
        return leftDead || currentHp <= 0;
    }

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
