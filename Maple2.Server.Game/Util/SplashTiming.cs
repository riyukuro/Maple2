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

    // What a fire of a region sends. The client plays the region's hit effect for every region damage packet,
    // before it reads the targets (CSceneStateGame::OnRegionSkillDamage), and KMS2 sends one for each fire: four
    // in five name no target, and no target packet goes before them. A cube skill's fire (the tile packet) is
    // sent with its targets only, after their target packet, as before.
    public static (bool Targets, bool Damage) FirePackets(bool cube, int targetCount) {
        return cube ? (targetCount > 0, targetCount > 0) : (false, true);
    }
}
