namespace Maple2.Server.Game.Util;

/// <summary>
/// The combatTime condition of the server AI files (69 conditions in 36 files). The time is the battle time: the
/// milliseconds since the NPC went into battle, which a setData node with key aiBattleTime sets (54 nodes, values
/// 0 and 32000 to 128000; the file comments say the reset lines battleTimeBegin=0 up with a phase start).
/// battleTimeLoop folds the time into a loop; a window passes from begin up to, not including, end (the 0-13000,
/// 13000-26000, ... windows of one loop of 52000 in AI_DevilHugeRed share their edges). 3 conditions give no
/// end: their window runs to the end of the loop.
/// </summary>
public static class AiCombatTime {
    public const string BattleTimeKey = "aiBattleTime";

    public static bool Passes(long battleTime, long begin, long loop, long end) {
        if (battleTime < 0) {
            return false;
        }
        long time = loop > 0 ? battleTime % loop : battleTime;
        if (end <= 0) {
            return time >= begin;
        }
        return time >= begin && time < end;
    }
}
