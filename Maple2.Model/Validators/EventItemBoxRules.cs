namespace Maple2.Model.Validators;

// The item box event (GameEventItemBox). Its dialog (client uiitemeventopenboxdialog.cpp:164) builds today as
// year * 10000 + month * 100 + day from the server's time and opens only while the day the box was last taken
// (user value 1400) is before it.
public static class EventItemBoxRules {
    public static int Day(DateTime time) => time.Year * 10000 + time.Month * 100 + time.Day;
    public static bool CanOpen(int lastDay, int today) => lastDay < today;
}
