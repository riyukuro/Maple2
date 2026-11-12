namespace Maple2.Server.Game.Util;

// When a splash sub-skill's region fires for the first time, in ms after the region is placed.
public static class SplashTiming {
    // immediateActive: at once, Delay is not used.
    // otherwise: Delay + Interval; the later fires follow every Interval.
    // onlySensingActive: the region starts looking for a target one Interval after it is placed and Delay only
    // bounds how long it looks (chosen here).
    public static int FirstFire(bool immediateActive, bool onlySensingActive, int delay, int interval) {
        if (immediateActive) {
            return 0;
        }
        if (onlySensingActive) {
            return interval;
        }
        return delay + interval;
    }
}
