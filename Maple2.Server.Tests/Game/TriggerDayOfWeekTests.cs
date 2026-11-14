using System;
using System.Runtime.CompilerServices;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Trigger;

namespace Maple2.Server.Tests.Game;

// The day_of_week condition (1 is Sunday) is the server's day, as the server's resets and events run in its local
// time. It took the UTC day, so around midnight it gave the next day west of UTC (the previous one east of it).
public class TriggerDayOfWeekTests {
    // An instant at which the local day and the UTC day differ: 23:30 local west of UTC, 00:30 local east of it.
    private static DateTimeOffset DayApart() {
        DateTime date = DateTime.Today;
        TimeSpan offset = TimeZoneInfo.Local.GetUtcOffset(date);
        DateTime local = date + (offset < TimeSpan.Zero ? new TimeSpan(23, 30, 0) : new TimeSpan(0, 30, 0));
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }

    [Test]
    public void DayOfWeek_IsTheLocalDay() {
        Assume.That(Math.Abs(TimeZoneInfo.Local.GetUtcOffset(DateTime.Today).TotalMinutes), Is.GreaterThan(30), "the host is on UTC");
        DateTimeOffset now = DayApart();
        var owner = (FieldTrigger) RuntimeHelpers.GetUninitializedObject(typeof(FieldTrigger));
        TriggerContext context = TriggerTestBase.UninitializedContext(owner);
        context.Clock = () => now;
        int localDay = (int) now.LocalDateTime.DayOfWeek + 1;
        int utcDay = (int) now.UtcDateTime.DayOfWeek + 1;
        Assert.That(localDay, Is.Not.EqualTo(utcDay));

        Assert.That(context.DayOfWeek([localDay], "", false), Is.True);
        Assert.That(context.DayOfWeek([utcDay], "", false), Is.False);
        Assert.That(context.DayOfWeek([localDay], "", true), Is.False);
    }
}
