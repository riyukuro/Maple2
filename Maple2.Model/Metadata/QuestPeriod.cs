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

    // The usePeriod that is not a day name read as minutes, as the client's atoi (FUN_140718170): leading blanks, a
    // sign, then the digits up to the first other character; 0 when there are none.
    public static int DurationMinutes(string? usePeriod) {
        if (string.IsNullOrEmpty(usePeriod) || ResetWeekday(usePeriod) != 0) {
            return 0;
        }
        int i = 0;
        while (i < usePeriod.Length && usePeriod[i] is ' ' or '\t' or '\n' or '\v' or '\f' or '\r') {
            i++;
        }
        bool negative = false;
        if (i < usePeriod.Length && usePeriod[i] is '+' or '-') {
            negative = usePeriod[i] == '-';
            i++;
        }
        long value = 0;
        for (; i < usePeriod.Length && usePeriod[i] is >= '0' and <= '9'; i++) {
            value = Math.Min(value * 10 + (usePeriod[i] - '0'), int.MaxValue);
        }
        return (int) (negative ? -value : value);
    }
}
