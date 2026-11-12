namespace Maple2.Model.Validators;

// Rest exp spent on an exp gain, as KMS2 sends it in ExpUp: every gain takes its own amount off the rest exp (down to 0).
// Only a kill gets a bonus, and only a whole one: the gain times 1 + RestExpAcquireRate / 10000 (3x at 20000) when the
// rest exp covers the larger gain, else the plain gain. Quests, chests and the other sources never get a bonus.
public static class RestExpRules {
    public static (long Gain, long RestLeft) Spend(long gain, long rest, bool kill, int acquireRate) {
        if (gain <= 0) {
            return (gain, rest);
        }
        if (kill && rest > 0 && acquireRate > 0) {
            long boosted = gain + (long) (gain * (acquireRate / 10000.0));
            if (rest >= boosted) {
                gain = boosted;
            }
        }
        return (gain, Math.Max(0, rest - gain));
    }

    // KMS2 adds rest exp at home every TickMs: the level's row of exp base table 1 times 35001 / 10000000, rounded
    // down and at least 1, up to NextExp times RestExpMaxAcquireRate / 10000. Returns the new rest exp.
    public const int TickMs = 10000;
    public const int TickTableId = 1;

    public static long Tick(long rest, long expBase, long nextExp, int maxAcquireRate) {
        long cap = nextExp * maxAcquireRate / 10000;
        if (rest >= cap) {
            return rest;
        }
        long step = Math.Max(1, expBase * 35001 / 10000000);
        return Math.Min(cap, rest + step);
    }
}
