using Maple2.Model.Enum;

namespace Maple2.Server.Game.Util;

// When the server reports the trophy conditions use_merat and dungeon_first_clear.
public static class TrophyReportRules {
    // The merets a balance change spent: 0 for a gain or no change.
    public static long MeretSpent(long delta) {
        return delta < 0 ? -delta : 0;
    }

    // Whether a move to the own home counts for home_goto: not when the player is already there.
    public static bool CountsAsGoHome(bool alreadyInOwnHome) {
        return !alreadyInOwnHome;
    }

    // Whether a trophy takes progress at all: a locked one does not.
    public static bool Counts(Maple2.Model.Metadata.AchievementMetadata metadata) {
        return !metadata.Locked;
    }

    // The decoration exp a home check-in adds to interior_exp_offset: the check-in's decoration score, when the home
    // took it (a home at the top decoration level takes none).
    public static long InteriorExpCounted(long decorationScore, bool homeGainedExp) {
        return homeGainedExp && decorationScore > 0 ? decorationScore : 0;
    }

    // Whether a check-in counts for interior_level: once when the decoration level rose, by however many levels.
    public static bool InteriorLevelRaised(long levelBefore, long levelAfter) {
        return levelAfter > levelBefore;
    }

    // Whether a clear that left the record at totalClears is the dungeon's first. A colosseum record keeps the round
    // in TotalClears, so it never reports.
    public static bool IsFirstClear(DungeonGroupType groupType, int totalClears) {
        return groupType != DungeonGroupType.colosseum && totalClears == 1;
    }
}
