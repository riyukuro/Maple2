using Maple2.Model.Enum;

namespace Maple2.Server.Game.Util;

// When the server reports the trophy conditions use_merat and dungeon_first_clear.
public static class TrophyReportRules {
    // The merets a balance change spent: 0 for a gain or no change.
    public static long MeretSpent(long delta) {
        return delta < 0 ? -delta : 0;
    }

    // Whether a clear that left the record at totalClears is the dungeon's first. A colosseum record keeps the round
    // in TotalClears, so it never reports.
    public static bool IsFirstClear(DungeonGroupType groupType, int totalClears) {
        return groupType != DungeonGroupType.colosseum && totalClears == 1;
    }
}
