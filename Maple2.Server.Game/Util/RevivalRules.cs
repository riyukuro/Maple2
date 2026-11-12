namespace Maple2.Server.Game.Util;

public enum MesoReviveRefusal {
    None,
    NotDead,
    NoRevivalHere,
    Buff,
    DebuffMax,
    NoVoucher,
    DailyMax,
    LackMeso,
}

public enum MeretReviveRefusal {
    None,
    NotDead,
    NoRevivalHere,
    Dungeon,
    Buff,
    Locale,
    DebuffMax,
    NoCoupon,
    LackMeret,
}

public static class RevivalRules {
    // The revive in place for merets or a coupon (Revival mode 1). Every test comes before anything is taken.
    // localeClosed is the constant LimitMeratRevival; debuffAllows is DebuffAllows of the meret revival debuff.
    public static MeretReviveRefusal MeretRevive(bool dead, bool noRevivalHere, bool dungeonDisables, bool buffBlocks,
            bool localeClosed, bool debuffAllows, bool useCoupon, bool hasCoupon, long meret, long cost) {
        if (!dead) {
            return MeretReviveRefusal.NotDead;
        }
        if (noRevivalHere) {
            return MeretReviveRefusal.NoRevivalHere;
        }
        if (dungeonDisables) {
            return MeretReviveRefusal.Dungeon;
        }
        if (buffBlocks) {
            return MeretReviveRefusal.Buff;
        }
        if (localeClosed) {
            return MeretReviveRefusal.Locale;
        }
        if (!debuffAllows) {
            return MeretReviveRefusal.DebuffMax;
        }
        if (useCoupon) {
            return hasCoupon ? MeretReviveRefusal.None : MeretReviveRefusal.NoCoupon;
        }
        return meret < cost || cost < 0 ? MeretReviveRefusal.LackMeret : MeretReviveRefusal.None;
    }

    // Client UIRevivalDialog::RefreshMesoButton: the Lua price scaled by 1 - min(fee reduce limit, the
    // character's meso fee reduce rate), never below 0, and 0 in a home field (map type 3).
    // A limit row of 0 means no reduction; only a missing row counts as 0.5 (the ConstantsTable default).
    public static long MesoCost(long luaCost, float reduceLimit, float reduceRate, bool homeField) {
        if (homeField) {
            return 0;
        }
        return MeretCost(luaCost, reduceLimit, reduceRate);
    }

    // The revive in place for mesos or a voucher (Revival mode 2). Every test comes before anything is
    // taken: a request from a living player used to take the mesos or the voucher and raise the daily count.
    // A voucher is not held to the daily count; mesos are, except in a home field: there the client shows the
    // free meso button before it looks at the count left (UIRevivalDialog::FUN_141abcb90). The client's meso
    // button also refuses while the meret revival debuff is on the player (UIRevivalDialog::FUN_141abc080), as
    // the meret button does.
    public static MesoReviveRefusal MesoRevive(bool dead, bool noRevivalHere, bool buffBlocks, bool useVoucher,
            bool hasVoucher, int usedToday, int dailyMax, long meso, long cost, bool debuffAllows = true,
            bool homeField = false) {
        if (!dead) {
            return MesoReviveRefusal.NotDead;
        }
        if (noRevivalHere) {
            return MesoReviveRefusal.NoRevivalHere;
        }
        if (buffBlocks) {
            return MesoReviveRefusal.Buff;
        }
        if (!debuffAllows) {
            return MesoReviveRefusal.DebuffMax;
        }
        if (useVoucher) {
            return hasVoucher ? MesoReviveRefusal.None : MesoReviveRefusal.NoVoucher;
        }
        if (!homeField && usedToday >= dailyMax) {
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

    // The meret and meso revive buttons are closed while the debuff the meret revive leaves is on the player
    // and enabled, at any stack count (the client asks AdditionalCliList::FUN_140970020(code, 0, 0, 1), whose
    // count of 0 skips the stack compare), unless the field has infinityMeratRevival. A debuff code of 0 (no
    // row in the constants) never closes them.
    public static bool DebuffAllows(int debuffCode, bool debuffEnabled, bool infiniteField) {
        if (infiniteField || debuffCode == 0) {
            return true;
        }
        return !debuffEnabled;
    }

    // The daily meso revive count rises for a meso or voucher revive, not for the free one of a home field.
    public static bool CountsTowardDailyMax(bool homeField) => !homeField;
}
