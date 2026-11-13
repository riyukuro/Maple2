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
// mission scores capped at maxScore, the records keep the best score, and the season week ends on the season's
// dayOfWeeks at the time of day the season starts.
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

    // DungeonRankRequire::GetRank (dungeonroom.cpp): the highest rank whose score the run reaches; below the
    // first one the rank is F. The table's ranks are C, B, A, S and S+ in order.
    public static DungeonMissionRank Rank(IReadOnlyList<DungeonMissionRankMetadata.Score> scores, short score) {
        for (int i = scores.Count - 1; i >= 0; i--) {
            if (score >= scores[i].Value) {
                return (DungeonMissionRank) (i + 1);
            }
        }
        return DungeonMissionRank.F;
    }

    // DungeonRoomData::IsSeasonRankRoom: a chaos raid whose limitPlayerLevel is at least DungeonSeasonRankMinLevel.
    public static bool IsSeasonRankRoom(DungeonGroupType groupType, int limitPlayerLevel, int seasonRankMinLevel) {
        return groupType == DungeonGroupType.chaosRaid && limitPlayerLevel >= seasonRankMinLevel;
    }

    // LifetimeRecord starts at -1 (no scored clear) and keeps the best score.
    public static void ApplyLifetime(DungeonRecord record, short score) {
        record.LifetimeRecord = Math.Max(record.LifetimeRecord, score);
    }

    // The end of the season week holding now: the season's next dayOfWeeks (Sunday = 1, the client's
    // CMsTime::GetDayOfWeek convention for limitDayOfWeeks; two of the season starts in the table fall on a Friday,
    // which fits 6, but the client code that reads this field was not found) at the season's reset time, clamped to
    // the season's end. The reset time is the time of day of the season's startTime: the KR seasons 1, 2 and 1001
    // start at 00:00 and the current one, 1002, at 08:00 (it follows 1001, which ends at 07:59:59). A dayOfWeeks
    // outside 1-7 uses the server's weekly reset day.
    public static DateTime SeasonExpiry(DateTime now, int dayOfWeeks, TimeSpan resetTime, DateTime seasonEnd) {
        DayOfWeek day = dayOfWeeks is >= 1 and <= 7 ? (DayOfWeek) (dayOfWeeks - 1) : Constant.ResetDay;
        int days = ((int) day - (int) now.DayOfWeek + 7) % 7;
        DateTime reset = now.Date.AddDays(days) + resetTime;
        if (reset <= now) {
            reset = reset.AddDays(7);
        }
        return reset < seasonEnd ? reset : seasonEnd;
    }

    // CurrentRecord keeps the best score of the season week: the live value (0 once its expiry has passed) or
    // this clear's score, whichever is higher, and its expiry moves to the end of the week.
    public static void ApplySeason(DungeonRecord record, short score, long now, long expiry) {
        short live = now < record.CooldownTimestamp ? record.CurrentRecord : (short) 0;
        record.CurrentRecord = Math.Max(live, score);
        record.CooldownTimestamp = expiry;
    }

    // The season total: the sum of the live CurrentRecords of the player's season rank rooms, as the client adds it
    // (DungeonRecordManagerCli::GetTotalScore, dungeonrecordcli.cpp:580: IsSeasonRankRoom records, Get68 at the
    // server time, scores above 0). The client never calls it and has no cap; the cap at the season's
    // maxSeasonScore (when set) is the server's reading of that name.
    public static int SeasonTotal(IEnumerable<short> scores, int maxSeasonScore) {
        int total = 0;
        foreach (short score in scores) {
            if (score > 0) {
                total += score;
            }
        }
        return maxSeasonScore > 0 ? Math.Min(total, maxSeasonScore) : total;
    }

    // The season weeks that have ended with a score: season rank records whose CurrentRecord has expired (now at or
    // after its expiry) and is above 0, by the week's end (the expiry). The week's total is then SeasonTotal of them.
    public static IEnumerable<IGrouping<long, DungeonRecord>> FinishedWeeks(IEnumerable<DungeonRecord> seasonRecords, long now) {
        return seasonRecords
            .Where(record => record.CurrentRecord > 0 && record.CooldownTimestamp > 0 && now >= record.CooldownTimestamp)
            .GroupBy(record => record.CooldownTimestamp)
            .OrderBy(week => week.Key);
    }
}
