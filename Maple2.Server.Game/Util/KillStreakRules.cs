using Maple2.Server.Game.LuaFunctions;

namespace Maple2.Server.Game.Util;

// The kill streak behind the client's kill count display (UIKillCount, shown from 3 kills) and the trophy 21210014
// (condition killcount). The client counts with its Lua calcKillCount; KMS2 sends the trophy the streak's length each
// time a streak passes the longest one so far. In KMS2 packets every kill that kept a streak came less than 5 s after
// the kill before it (longest 4.97 s) and every kill that broke one came later (shortest 5.98 s).
public static class KillStreakRules {
    // The streak after a kill, elapsedMs after the kill before it. The first kill starts a streak of 1.
    public static int Next(int streak, int playerLevel, int npcLevel, long elapsedMs) {
        if (streak <= 0) {
            return 1;
        }
        return Lua.CalcKillCount(playerLevel, npcLevel, (int) Math.Clamp(elapsedMs, 0, int.MaxValue), streak);
    }

    // The exp bonus of a streak, as a fraction (0.01 = +1%). In KMS2 kills it came in at the kill that made the streak
    // 21, 31, 41, 51 and 101 (8 of the 12 kills on those steps; the other 4 a kill later, as packet times blur 5 s).
    // The Lua rate is a float (0.01f is 0.0099999998); it is taken to the whole percent it stands for.
    public static double BonusRate(int streak) => Math.Round(Lua.CalcKillCountBonusExpRate(streak), 2);
}
