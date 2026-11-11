namespace Maple2.Model.Metadata;

public static class QuestPeriod {
    private static readonly string[] Days = ["sun", "mon", "tue", "wed", "thu", "fri", "sat"];

    // The quest's basic usePeriod as the client reads it (FUN_14028e160, commonutil.cpp:898): a day name, any case,
    // gives 1 (sun) to 7 (sat), the reset day of a weekly quest; anything else gives 0.
    public static int ResetWeekday(string? usePeriod) {
        if (string.IsNullOrEmpty(usePeriod)) {
            return 0;
        }
        int index = Array.FindIndex(Days, day => string.Equals(day, usePeriod, StringComparison.OrdinalIgnoreCase));
        return index + 1;
    }
}
