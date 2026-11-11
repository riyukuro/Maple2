using Maple2.Model.Enum;
using Maple2.Model.Game.Dungeon;
using Maple2.Model.Metadata;

namespace Maple2.Model.Validators;

// The dungeon rank score of a clear and the two records the client shows from it (DungeonRecord.WriteTo):
// LifetimeRecord (the client's m_60, the best score ever) and CurrentRecord with its expiry (m_68, a timed value:
// the best score of the season week, shown while now is before the expiry).
//
// Proven from the client: the rank table of a room (GetRankTableID), its maxScore (DungeonRankRequire), which
// rooms are season rank rooms (IsSeasonRankRoom) and the season rows (DungeonSeason::Load, GetCurrentSeason).
// Inferred, not found in the client (the server fills these records): the run score is the sum of the run's
// mission scores capped at maxScore, the records keep the best score, and the season week ends at midnight of
// the season's dayOfWeeks.
public static class DungeonScoreRules {
    // DungeonRoomData::GetRankTableID (dungeonroom.cpp): rankTableID when set, else by group type; 0 = no rank.
    public static int RankTableId(int rankTableId, DungeonGroupType groupType) {
        if (rankTableId != 0) {
            return rankTableId;
        }
        return groupType switch {
            DungeonGroupType.normal => 1,
            DungeonGroupType.raid => 2,
            DungeonGroupType.chaosRaid => 3,
            DungeonGroupType.reverseRaid => 4,
            DungeonGroupType.worldBossDungeon => 6,
            DungeonGroupType.fameChallenge => 5,
            _ => 0,
        };
    }

    // The run score: the sum of the mission scores at the clear, capped at the rank table's maxScore.
    public static short RunScore(IEnumerable<short> missionScores, int maxScore) {
        int sum = 0;
        foreach (short score in missionScores) {
            sum += Math.Max((short) 0, score);
        }
        return (short) Math.Clamp(Math.Min(sum, maxScore), 0, short.MaxValue);
    }

    // DungeonRoomData::IsSeasonRankRoom: a chaos raid whose limitPlayerLevel is at least DungeonSeasonRankMinLevel.
    public static bool IsSeasonRankRoom(DungeonGroupType groupType, int limitPlayerLevel, int seasonRankMinLevel) {
        return groupType == DungeonGroupType.chaosRaid && limitPlayerLevel >= seasonRankMinLevel;
    }

    // LifetimeRecord starts at -1 (no scored clear) and keeps the best score.
    public static void ApplyLifetime(DungeonRecord record, short score) {
        record.LifetimeRecord = Math.Max(record.LifetimeRecord, score);
    }

    // The end of the season week holding now: the next midnight of the season's dayOfWeeks (Sunday = 1, the
    // client's CMsTime::GetDayOfWeek convention for limitDayOfWeeks; two of the season starts in the table fall on a
    // Friday, which fits 6, but the client code that reads this field was not found), clamped to the season's end.
    // A dayOfWeeks outside 1-7 uses the server's weekly reset day.
    public static DateTime SeasonExpiry(DateTime now, int dayOfWeeks, DateTime seasonEnd) {
        DayOfWeek day = dayOfWeeks is >= 1 and <= 7 ? (DayOfWeek) (dayOfWeeks - 1) : Constant.ResetDay;
        int days = ((int) day - (int) now.DayOfWeek + 7) % 7;
        DateTime reset = now.Date.AddDays(days == 0 ? 7 : days);
        return reset < seasonEnd ? reset : seasonEnd;
    }

    // CurrentRecord keeps the best score of the season week: the live value (0 once its expiry has passed) or
    // this clear's score, whichever is higher, and its expiry moves to the end of the week.
    public static void ApplySeason(DungeonRecord record, short score, long now, long expiry) {
        short live = now < record.CooldownTimestamp ? record.CurrentRecord : (short) 0;
        record.CurrentRecord = Math.Max(live, score);
        record.CooldownTimestamp = expiry;
    }
}
