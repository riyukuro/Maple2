using Maple2.Model.Metadata;

namespace Maple2.Model.Validators;

// The snowman event (GameEventSnowman). Its dialog (client uisnowmaneventdialog.cpp) reads four user values of the
// event id, each a comma list (ParseIntArray) by snowman index: 1700 the snowflakes, 1701 today's completes, 1702
// all completes and 1703 the indexes whose accum reward was taken. A missing entry reads 0.
public static class SnowmanRules {
    public static int[] Counts(string? value, int length) {
        var counts = new int[length];
        if (string.IsNullOrEmpty(value)) {
            return counts;
        }
        string[] parts = value.Split(',');
        for (int i = 0; i < length && i < parts.Length; i++) {
            counts[i] = int.TryParse(parts[i], out int count) ? count : 0;
        }
        return counts;
    }

    public static string Join(IEnumerable<int> counts) => string.Join(",", counts);

    public static HashSet<int> Indexes(string? value) {
        return string.IsNullOrEmpty(value) ? []
            : value.Split(',').Select(part => int.TryParse(part, out int index) ? index : -1).Where(index => index >= 0).ToHashSet();
    }

    // SnowmanContext::Snowman::CanProceed (gameeventcontext.cpp): a snowman takes snowflakes while it is short of
    // its goal and today's completes are below the daily limit.
    public static bool CanProceed(Snowman.Entry snowman, int snowflakes, int dailyCompletes) {
        return snowflakes < snowman.Goal && dailyCompletes < snowman.DailyMax;
    }

    // The snowflakes after a source's points: every snowman that can proceed takes them, up to its goal. False when
    // none could.
    public static bool Gain(Snowman snowman, int[] snowflakes, int[] dailyCompletes, int point) {
        bool gained = false;
        for (int i = 0; i < snowman.Snowmen.Length; i++) {
            Snowman.Entry entry = snowman.Snowmen[i];
            if (!CanProceed(entry, snowflakes[i], dailyCompletes[i])) {
                continue;
            }
            snowflakes[i] = (int) Math.Min(entry.Goal, (long) snowflakes[i] + point);
            gained = true;
        }
        return gained;
    }

    // The daily complete (EventReward 12): the snowman has its goal (ReachedGoal) and today's completes are below
    // the limit (BelowLimit), the two tests of the dialog's buttons.
    public static bool CanClaimDaily(Snowman.Entry snowman, int snowflakes, int dailyCompletes) {
        return snowflakes >= snowman.Goal && dailyCompletes < snowman.DailyMax;
    }

    // The accum reward (EventReward 13): all completes reach the accum count (ReachedBonus) and it was not taken.
    public static bool CanClaimAccum(Snowman.Entry snowman, int accumCompletes, bool received) {
        return accumCompletes >= snowman.AccumMax && !received;
    }
}
