namespace Maple2.Server.Game.Util;

// Decorating exp (exp type construct, commonexptable factor 0.05 on curve 1 in KR): a cube the player places in a home,
// at most the constant ConstructExpMaxCount times a day (100 in KR); past that the error
// s_err_ugcmap_construct_exp_overtime ("no more experience from decorating today"). The client only names the error;
// the server-side feature ConstructExp (192) is left to the ingest, which drops the construct row when it is off.
public static class ConstructExpRules {
    public enum Step {
        None, // no exp and no notice
        Exp, // the placement's exp, counted
        OverTime, // the day's cap was reached: the notice, once a day
    }

    // paidToday: the placements already paid today (PetFusionRules.DailyCount of the stored pair). Chosen here: the
    // notice is sent for the first placement past the cap only, which counts it too (cap + 1), so a decorating player
    // does not get it for every cube; a cap of 0 or below (no constant) gives no exp and no notice.
    public static Step Check(bool hasRow, int paidToday, int maxPerDay) {
        if (!hasRow || maxPerDay <= 0) {
            return Step.None;
        }
        if (paidToday < maxPerDay) {
            return Step.Exp;
        }
        return paidToday == maxPerDay ? Step.OverTime : Step.None;
    }
}
