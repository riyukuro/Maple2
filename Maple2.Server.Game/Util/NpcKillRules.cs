using System.Globalization;
using Maple2.Model.Metadata;

namespace Maple2.Server.Game.Util;

public static class NpcKillRules {
    // npc_lasthit_time's target is a clock time, HH:mm (21230022 "23:59"); the client has no code for it, so the
    // server's local time at the killing blow is the one compared.
    public static string LastHitTime(DateTime localTime) {
        return localTime.ToString("HH:mm", CultureInfo.InvariantCulture);
    }

    // npc_race: a kill counts once per race in the npc's raceString (the trophies 21210002-21210012 have the codes
    // animal, plant, bug, mystic, spirit, fairy, undead, devil, machine, creature and combine); its mainTags are
    // names of single npcs and match none.
    public static IReadOnlyList<string> RaceCodes(NpcMetadataBasic basic) {
        return basic.RaceStrings ?? [];
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
