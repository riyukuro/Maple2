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
}
