using System;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;

namespace Maple2.Server.Tests.Validators;

// The server's weekly reset (the World's schedule and the shops' weekly restock) is at 00:00 on Friday, the client's
// week start (Constant.ResetDay; NextWeekdayStart(6, ...) in the client's weekly quest and reset times).
public class WeeklyResetRulesTests {
    [Test]
    public void ResetDayIsFriday() {
        Assert.That(Constant.ResetDay, Is.EqualTo(DayOfWeek.Friday));
    }

    // 2026-11-12 is a Thursday, 2026-11-13 a Friday.
    [TestCase("2026-11-12 23:59:59", "2026-11-06 00:00", "2026-11-13 00:00")] // Thursday night: the week before's
    [TestCase("2026-11-13 00:00:00", "2026-11-13 00:00", "2026-11-20 00:00")] // at the reset: the new week
    [TestCase("2026-11-13 10:00:00", "2026-11-13 00:00", "2026-11-20 00:00")]
    [TestCase("2026-11-15 12:00:00", "2026-11-13 00:00", "2026-11-20 00:00")] // a Sunday
    [TestCase("2026-11-11 08:00:00", "2026-11-06 00:00", "2026-11-13 00:00")] // a Wednesday
    public void WeekStartsAtMidnightOnFriday(string now, string last, string next) {
        DateTime at = DateTime.Parse(now);
        Assert.That(WeeklyResetRules.LastStart(at), Is.EqualTo(DateTime.Parse(last)));
        Assert.That(WeeklyResetRules.NextStart(at), Is.EqualTo(DateTime.Parse(next)));
    }

    // The World's start runs the reset when the last one stored is before this week's start: a reset stored on
    // Thursday (the old reset day) does not stand for the week starting the next day.
    [Test]
    public void StoredThursdayResetIsBeforeTheFridayWeek() {
        DateTime storedThursday = DateTime.Parse("2026-11-12 00:00:05");
        Assert.That(storedThursday < WeeklyResetRules.LastStart(DateTime.Parse("2026-11-13 09:00")), Is.True);
        Assert.That(storedThursday < WeeklyResetRules.LastStart(DateTime.Parse("2026-11-12 09:00")), Is.False);
    }
}
