using Maple2.Model.Metadata;

namespace Maple2.Model.Validators;

// The server's weekly reset: 00:00 on Constant.ResetDay, in the server's local time like the daily reset.
public static class WeeklyResetRules {
    // The start of the week holding now: the last reset at or before now.
    public static DateTime LastStart(DateTime now) {
        int days = ((int) now.DayOfWeek - (int) Constant.ResetDay + 7) % 7;
        return now.Date.AddDays(-days);
    }

    // The next reset after now (a week after LastStart, so at a reset itself the one a week later).
    public static DateTime NextStart(DateTime now) => LastStart(now).AddDays(7);
}
