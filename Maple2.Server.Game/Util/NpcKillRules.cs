using System.Globalization;

namespace Maple2.Server.Game.Util;

public static class NpcKillRules {
    // npc_lasthit_time's target is a clock time, HH:mm (21230022 "23:59"); the client has no code for it, so the
    // server's local time at the killing blow is the one compared.
    public static string LastHitTime(DateTime localTime) {
        return localTime.ToString("HH:mm", CultureInfo.InvariantCulture);
    }

    // npc_timeattack: the whole seconds since the dungeon began (its first field's entry), or null outside a run that
    // has begun.
    public static long? TimeAttackSeconds(long startTick, long now) {
        if (startTick <= 0 || now < startTick) {
            return null;
        }
        return (now - startTick) / 1000;
    }
}
