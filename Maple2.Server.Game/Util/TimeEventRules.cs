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
}
