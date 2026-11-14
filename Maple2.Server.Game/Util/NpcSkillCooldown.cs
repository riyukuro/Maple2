namespace Maple2.Server.Game.Util;

/// The npc's skill coolDown (npc xml <skill coolDown>, 1400 KR npcs: 3000 on 605, 2000 on 462, 5000 on 166, 4000 on
/// 123): the least time between two of its AI skill casts. The client only reads its own npcdata, which has no such
/// value; the default AI trees (AI_DefaultNew*, the AI of 1200 of them) give their skill nodes no cooltime and stand
/// 500 to 2000 ms after a cast, so without it these npcs attack again as soon as that standby ends.
public static class NpcSkillCooldown {
    /// The time left until the npc may cast again: 0 with no coolDown, before its first cast, or once the time is up.
    public static long Remaining(int cooldown, long? lastCastTick, long tick) {
        if (cooldown <= 0 || lastCastTick is not long last) {
            return 0;
        }
        return Math.Max(0, last + cooldown - tick);
    }
}
