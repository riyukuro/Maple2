namespace Maple2.Server.Game.Util;

// The account birthday (Account.Birthday). The client keeps it per character (PC+0x11e8: the field's add-user
// block, the character list entry and BirthdayCard 0x111 mode 1) and only ever reads its month: the birthday card
// (UIBirthdayCardDialog, npc kind 88) shows it when PC::FUN_141660030 finds that month equal to the server time's.
// Only the month and day are kept: the stored value is 12:00 UTC of that day in 2000, so the date is the same in
// every time zone within 12 hours of UTC.
public static class BirthdayRules {
    private const int Year = 2000; // a leap year, so 02-29 is valid

    // Unix seconds for the month and day, or null when they are not a date.
    public static long? Of(int month, int day) {
        if (month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(Year, month)) {
            return null;
        }
        return new DateTimeOffset(Year, month, day, 12, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
    }

    // The client's test (PC::FUN_141660030): a birthday is set and its month is the current month. The client takes
    // both months through CMsTime, in the zone TimeSyncPacket sends (the server's local zone, CMsTime::SetTimeZone):
    // the server time's (GetServerTime) and the birthday's. Both are taken here in that zone too, so the server and
    // the card agree at a month's end and in a zone more than 12 hours from UTC.
    public static bool IsBirthdayMonth(long birthday, DateTimeOffset now, TimeZoneInfo zone) {
        return birthday != 0
               && TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(birthday), zone).Month == TimeZoneInfo.ConvertTime(now, zone).Month;
    }
}
