namespace Maple2.Server.Game.Util;

// The account birthday (Account.Birthday). The client keeps it per character (PC+0x11e8: the field's add-user
// block, the character list entry and BirthdayCard 0x111 mode 1) and only ever reads its month: the birthday card
// (UIBirthdayCardDialog, npc kind 88) shows it when PC::FUN_141660030 finds that month equal to the server time's.
// Only the month and day are kept: the stored value is 12:00 UTC of that day in 2000, so the month is the same in
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

    // The client's test (PC::FUN_141660030): a birthday is set and its month is the current month.
    public static bool IsBirthdayMonth(long birthday, DateTime now) {
        return birthday != 0 && DateTimeOffset.FromUnixTimeSeconds(birthday).UtcDateTime.Month == now.Month;
    }
}
