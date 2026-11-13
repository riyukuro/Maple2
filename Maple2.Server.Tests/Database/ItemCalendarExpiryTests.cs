using System;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Tests.Game;

namespace Maple2.Server.Tests.Database;

// An item of weeks or months (life expirationType 1 or 2) ends on the calendar, the week or month it is made counting as
// the first (Item.CalendarExpiry). It used to last its count of weeks or months from the moment it was made, and a count
// of 0 never ended.
public class ItemCalendarExpiryTests {
    private static readonly TimeZoneInfo Kst = TimeZoneInfo.CreateCustomTimeZone("KST", TimeSpan.FromHours(9), "KST", "KST");

    private static long At(string kst) => DateTimeOffset.Parse(kst).ToUnixTimeSeconds();

    // KMS2 records: made at the first time, expiring at the second (count 2, builds 2486-2503).
    [TestCase("2020-12-27T19:26:29+09:00", 2, "2021-01-31T23:59:59+09:00")] // 20303212
    [TestCase("2020-12-31T03:32:13+09:00", 2, "2021-01-31T23:59:59+09:00")] // 30001181
    [TestCase("2021-06-01T05:39:00+09:00", 2, "2021-07-31T23:59:59+09:00")] // 20302049
    [TestCase("2021-03-26T01:22:00+09:00", 2, "2021-04-30T23:59:59+09:00")] // 33000355
    [TestCase("2021-02-05T14:37:00+09:00", 1, "2021-02-28T23:59:59+09:00")]
    [TestCase("2021-02-05T14:37:00+09:00", 0, "2021-02-28T23:59:59+09:00")] // 0 counts as 1
    public void Months_EndOnTheLastDay(string made, int count, string expiry) {
        var life = new ItemMetadataLife(0, 0, 2, count);

        Assert.That(Item.MadeExpiry(life, At(made), Kst), Is.EqualTo(At(expiry)));
    }

    // Chosen here: a week ends on the day before the weekly reset day (Thursday), 23:59:59.
    [TestCase("2021-06-02T10:00:00+09:00", 1, "2021-06-02T23:59:59+09:00")] // a Wednesday
    [TestCase("2021-06-03T00:00:00+09:00", 1, "2021-06-09T23:59:59+09:00")] // a Thursday
    [TestCase("2021-06-06T12:00:00+09:00", 2, "2021-06-16T23:59:59+09:00")] // a Sunday
    [TestCase("2021-06-06T12:00:00+09:00", 0, "2021-06-09T23:59:59+09:00")]
    public void Weeks_EndOnTheDayBeforeTheReset(string made, int count, string expiry) {
        var life = new ItemMetadataLife(0, 0, 1, count);

        Assert.That(Item.MadeExpiry(life, At(made), Kst), Is.EqualTo(At(expiry)));
    }

    // A made item takes the calendar end in the server's zone, not its count of months from now.
    [Test]
    public void MadeItem_EndsAtTheEndOfNextMonth() {
        ItemMetadata metadata = TestGameSession.Metadata(20303212) with { Life = new ItemMetadataLife(5097600, 0, 2, 2) };

        var item = new Item(metadata);

        DateTime end = TimeZoneInfo.ConvertTimeFromUtc(DateTimeOffset.FromUnixTimeSeconds(item.ExpiryTime).UtcDateTime, TimeZoneInfo.Local);
        DateTime now = DateTime.Now;
        DateTime next = new DateTime(now.Year, now.Month, 1).AddMonths(1);
        Assert.Multiple(() => {
            Assert.That((end.Year, end.Month, end.Day), Is.EqualTo((next.Year, next.Month, DateTime.DaysInMonth(next.Year, next.Month))));
            Assert.That(end.TimeOfDay, Is.EqualTo(new TimeSpan(23, 59, 59)));
        });
    }

    // A usePeriod item still lasts its minutes from now.
    [Test]
    public void UsePeriod_LastsFromNow() {
        Assert.That(Item.MadeExpiry(new ItemMetadataLife(2592000, 0), 1000, Kst), Is.EqualTo(1000 + 2592000));
    }
}
