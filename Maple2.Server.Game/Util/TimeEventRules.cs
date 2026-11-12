using Maple2.Model.Enum;

namespace Maple2.Server.Game.Util;

public static class TimeEventRules {
    // The fields whose players get the global portal announce, when the portal opens and when they enter a field
    // while it is open. KMS2 sends it again at each field enter during the opening.
    public static bool PortalAnnouncedIn(MapType type) {
        return type is >= MapType.None and <= MapType.Telescope or >= MapType.Alikar and <= MapType.Shelter;
    }

    // A world boss at the end of its LifeTime leaves when nobody hit it within the idle threshold; a boss that was
    // hit later stays (and gets its notices from the idle monitor).
    public static bool BossLeaves(long lastDamageTick, long nowTick, TimeSpan idleThreshold) {
        return lastDamageTick <= 0 || nowTick - lastDamageTick >= (long) idleThreshold.TotalMilliseconds;
    }

    // The close comes warningDelay after the warning. Only a boss that was idle at the warning (so its players were
    // warned) and was not hit since leaves then; a boss that passed the idle threshold between the two stays for the
    // idle monitor, which warns before it lets the boss leave.
    public static bool BossLeavesAtClose(long lastDamageTick, long nowTick, TimeSpan idleThreshold, TimeSpan warningDelay) {
        return BossLeaves(lastDamageTick, nowTick, idleThreshold + warningDelay);
    }
}
