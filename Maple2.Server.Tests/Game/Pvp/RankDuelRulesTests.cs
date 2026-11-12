using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Game.Pvp;
using Maple2.Model.Metadata;

namespace Maple2.Server.Tests.Game.Pvp;

// The rank duel record rules: grades (CRankingArenaDataTable::GetGradeOfRating), rating and token changes, the
// weekly cap (the dialog's gauge), the season (CSeasonDataTable::Refresh) and the queue's widening window.
public class RankDuelRulesTests {
    // The rows of table/pvprankingduelmode.xml.
    private static readonly RankDuelGradeTable Grades = new(new Dictionary<int, RankDuelGradeTable.Grade> {
        [1] = new(1, 1000, 10, 10, 600, 30, -10),
        [2] = new(2, 1060, 20, 15, 1000, 30, -15),
        [3] = new(3, 1300, 40, 20, 1800, 30, -20),
        [4] = new(4, 1800, 50, 20, 2300, 30, -30),
        [5] = new(5, 2200, 60, 20, 2700, 30, -30),
        [6] = new(6, 2800, 70, 20, 3100, 30, -30),
        [7] = new(7, 3700, 80, 20, 3600, 30, -30),
    });

    [TestCase(0, 1)]
    [TestCase(999, 1)]
    [TestCase(1000, 1)]
    [TestCase(1059, 1)]
    [TestCase(1060, 2)]
    [TestCase(1299, 2)]
    [TestCase(3699, 6)]
    [TestCase(3700, 7)]
    [TestCase(9999, 7)]
    public void GradeOf_TheHighestGradeAtOrBelowTheRating(int rating, int grade) {
        Assert.That(RankDuelRules.GradeOf(Grades, rating)!.Id, Is.EqualTo(grade));
    }

    // The change and the tokens come from the duelist's own grade before the result.
    [Test]
    public void Apply_ByOwnGrade() {
        var low = new RankDuelRecord { Season = 1, Rating = 1050 };
        Assert.That(RankDuelRules.Apply(low, Grades, true, 10, true), Is.EqualTo(new RankDuelChange(1080, 30, 10)));
        Assert.That(low.BestRating, Is.EqualTo(1080));
        Assert.That(RankDuelRules.Apply(low, Grades, false, 20, true), Is.EqualTo(new RankDuelChange(1065, -15, 15)));
        Assert.That(low.BestRating, Is.EqualTo(1080));
        Assert.That(low.WeeklyReward, Is.EqualTo(25));
        Assert.That((low.Jobs[10].Wins, low.Jobs[10].Losses, low.Jobs[20].Wins, low.Jobs[20].Losses), Is.EqualTo((1, 0, 0, 1)));
    }

    // The tokens stop at the grade's weeklyRewardMax; the rating still moves.
    [Test]
    public void Apply_WeeklyCap() {
        var record = new RankDuelRecord { Rating = 1000, WeeklyReward = 595 };
        Assert.That(RankDuelRules.Apply(record, Grades, true, 10, true), Is.EqualTo(new RankDuelChange(1030, 30, 5)));
        Assert.That(record.WeeklyReward, Is.EqualTo(600));
        Assert.That(RankDuelRules.Apply(record, Grades, false, 10, true), Is.EqualTo(new RankDuelChange(1020, -10, 0)));
    }

    // One who left or never came: the rating change, no tokens.
    [Test]
    public void Apply_NotRewarded() {
        var record = new RankDuelRecord { Rating = 1000 };
        Assert.That(RankDuelRules.Apply(record, Grades, false, 10, false), Is.EqualTo(new RankDuelChange(990, -10, 0)));
        Assert.That(record.WeeklyReward, Is.EqualTo(0));
    }

    [Test]
    public void Apply_RatingNotBelowZero() {
        var record = new RankDuelRecord { Rating = 5 };
        Assert.That(RankDuelRules.Apply(record, Grades, false, 10, true).Rating, Is.EqualTo(0));
    }

    // A new season starts the record over (rating and best 1000, no job rows); the weekly tokens stay.
    [Test]
    public void StartSeason() {
        var record = new RankDuelRecord { Season = 80, Rating = 1500, BestRating = 1600, WeeklyReward = 40 };
        record.Jobs[10] = new RankDuelJobRecord { Wins = 2 };
        Assert.That(record.StartSeason(80), Is.False);
        Assert.That(record.Rating, Is.EqualTo(1500));
        Assert.That(record.StartSeason(81), Is.True);
        Assert.That((record.Season, record.Rating, record.BestRating, record.WeeklyReward, record.Jobs.Count), Is.EqualTo((81, 1000, 1000, 40, 0)));
    }

    // The season whose span holds now (start inclusive, end exclusive); 0 when none does, as the client finds it.
    [Test]
    public void SeasonAt() {
        var seasons = new Dictionary<int, SeasonDataTable.Entry> {
            [80] = new(80, new DateTime(2022, 11, 17, 7, 0, 0), new DateTime(2022, 12, 15, 7, 0, 0), []),
            [81] = new(81, new DateTime(2022, 12, 15, 7, 0, 0), new DateTime(2023, 1, 12, 7, 0, 0), []),
        };
        Assert.That(RankDuelRules.SeasonAt(seasons, new DateTime(2022, 12, 1)), Is.EqualTo(80));
        // At the end's own second the client still holds the ending season (it moves on at now > end).
        Assert.That(RankDuelRules.SeasonAt(seasons, new DateTime(2022, 12, 15, 7, 0, 0)), Is.EqualTo(80));
        Assert.That(RankDuelRules.SeasonAt(seasons, new DateTime(2022, 12, 15, 7, 0, 1)), Is.EqualTo(81));
        Assert.That(RankDuelRules.SeasonAt(seasons, new DateTime(2026, 11, 12)), Is.EqualTo(0));
        Assert.That(RankDuelRules.SeasonAt(seasons, new DateTime(2016, 1, 1)), Is.EqualTo(0));
    }

    // The window: minGap, wider by minGap every matchingTime seconds; no constants, no limit.
    [Test]
    public void AllowedGap() {
        Assert.That(RankDuelRules.AllowedGap(0, 2, 100), Is.EqualTo(100));
        Assert.That(RankDuelRules.AllowedGap(1999, 2, 100), Is.EqualTo(100));
        Assert.That(RankDuelRules.AllowedGap(2000, 2, 100), Is.EqualTo(200));
        Assert.That(RankDuelRules.AllowedGap(5000, 0, 100), Is.EqualTo(100));
        Assert.That(RankDuelRules.AllowedGap(0, 2, 0), Is.EqualTo(long.MaxValue));
    }

    // Pairing by rating: far ratings wait until the window has grown; the closest rating inside it is taken.
    [Test]
    public void Queue_PairsByRatingWithAWideningWindow() {
        var queue = new RankDuelQueue { MatchingTimeSeconds = 2, MatchingMinGap = 100 };
        Assert.That(queue.Register(1, 0, 1000, out List<RankDuelMatch> made), Is.EqualTo(RankDuelError.none));
        Assert.That(made, Is.Empty);
        queue.Register(2, 0, 1250, out made);
        Assert.That(made, Is.Empty);
        // 1: waited 2 s, window 200: 1250 still out. At 4 s, window 300: paired.
        Assert.That(queue.Pair(2000), Is.Empty);
        List<RankDuelMatch> pair = queue.Pair(4000);
        Assert.That(pair.Single().Characters, Is.EquivalentTo(new long[] { 1, 2 }));

        queue.Register(3, 5000, 1000, out _);
        queue.Register(4, 5000, 1090, out _);
        Assert.That(queue.IsQueued(3), Is.False);
        Assert.That(queue.MatchOf(3)!.Characters, Is.EquivalentTo(new long[] { 3, 4 }));

        // The closest rating inside the window goes first.
        queue.Register(5, 6000, 1000, out _);
        queue.Register(6, 6001, 1300, out _);
        queue.Register(7, 6002, 1050, out made);
        Assert.That(made.Single().Characters, Is.EquivalentTo(new long[] { 5, 7 }));
        Assert.That(queue.IsQueued(6), Is.True);
    }
}
