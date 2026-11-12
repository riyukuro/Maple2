using Maple2.Model.Metadata;

namespace Maple2.Model.Game.Pvp;

// A character's rank duel standing: the PvP season it belongs to, the rating and the season's best, the valor
// tokens won this week, and the season's wins and losses by the opponent's job code. Pvp 22 carries the first four
// (CPvpRankDuelComponent::OnMessage 0x16: league, rating, best, weekly), Pvp 23 the job rows (0x17).
public sealed class RankDuelRecord {
    // The client's start value (CPvpRankDuelComponent::Reset, RefreshLeague).
    public const int StartRating = 1000;

    public int Season { get; set; }
    public int Rating { get; set; } = StartRating;
    public int BestRating { get; set; } = StartRating;
    public int WeeklyReward { get; set; }
    public Dictionary<int, RankDuelJobRecord> Jobs { get; init; } = [];

    // A record of another season starts over, as the client's RefreshLeague shows it: rating and best at the
    // start value, no job rows. The weekly tokens keep their own (weekly) reset.
    public bool StartSeason(int season) {
        if (Season == season) {
            return false;
        }
        Season = season;
        Rating = StartRating;
        BestRating = StartRating;
        Jobs.Clear();
        return true;
    }
}

public sealed class RankDuelJobRecord {
    public int Wins { get; set; }
    public int Losses { get; set; }
}

// What one duelist's result did to its record.
public readonly record struct RankDuelChange(int Rating, int RatingChange, int Reward);

public static class RankDuelRules {
    // The grade a rating has (CRankingArenaDataTable::GetGradeOfRating): the highest grade whose gradeRating is at
    // most the rating, grade 1 below grade 1's.
    public static RankDuelGradeTable.Grade? GradeOf(RankDuelGradeTable table, int rating) {
        RankDuelGradeTable.Grade? found = null;
        foreach (RankDuelGradeTable.Grade grade in table.Grades.Values.OrderBy(grade => grade.Id)) {
            if (grade.GradeRating > rating) {
                break;
            }
            found = grade;
        }
        return found ?? table.Grades.Values.OrderBy(grade => grade.Id).FirstOrDefault();
    }

    // The current PvP season as the client finds it (CSeasonDataTable::Refresh): the season whose start is at or
    // before now and whose end is after it; 0 when none is.
    public static int SeasonAt(IReadOnlyDictionary<int, SeasonDataTable.Entry> seasons, DateTime now) {
        foreach (SeasonDataTable.Entry season in seasons.Values.OrderBy(season => season.StartTime)) {
            if (now >= season.StartTime && now < season.EndTime) {
                return season.Id;
            }
        }
        return 0;
    }

    // A win or a loss against an opponent of opponentJob. The rating moves by the duelist's own grade's
    // winnerRating or loserRating; the valor tokens are the grade's winnerReward or loserReward, as far as the
    // grade's weeklyRewardMax leaves room this week (the dialog's gauge, UIRankPvPDialog::UpdateOwnRecord).
    // Chosen here: the rating does not go below 0; a duelist who left or never came gets the rating change but no
    // tokens (rewarded = false).
    public static RankDuelChange Apply(RankDuelRecord record, RankDuelGradeTable table, bool won, int opponentJob, bool rewarded) {
        RankDuelGradeTable.Grade? grade = GradeOf(table, record.Rating);
        int change = grade == null ? 0 : won ? grade.WinnerRating : grade.LoserRating;
        int rating = Math.Max(0, record.Rating + change);
        change = rating - record.Rating;
        record.Rating = rating;
        record.BestRating = Math.Max(record.BestRating, rating);

        int reward = 0;
        if (rewarded && grade != null) {
            int room = Math.Max(0, grade.WeeklyRewardMax - record.WeeklyReward);
            reward = Math.Min(won ? grade.WinnerReward : grade.LoserReward, room);
            record.WeeklyReward += reward;
        }

        if (!record.Jobs.TryGetValue(opponentJob, out RankDuelJobRecord? job)) {
            record.Jobs[opponentJob] = job = new RankDuelJobRecord();
        }
        if (won) {
            job.Wins++;
        } else {
            job.Losses++;
        }
        return new RankDuelChange(rating, change, reward);
    }

    // The widening window of the queue: the gap allowed after waiting waitedMs, from the server constants
    // rankDuelPvpMatchingMinGap and rankDuelPvpMatchingTime (seconds). Chosen here (the constants are not
    // explained by the client): the gap starts at minGap and grows by minGap every matchingTime seconds; with no
    // constants every gap is allowed.
    public static long AllowedGap(long waitedMs, int matchingTimeSeconds, int minGap) {
        if (minGap <= 0) {
            return long.MaxValue;
        }
        long steps = matchingTimeSeconds <= 0 ? 0 : Math.Max(0, waitedMs) / (matchingTimeSeconds * 1000L);
        return minGap * (1 + steps);
    }
}
