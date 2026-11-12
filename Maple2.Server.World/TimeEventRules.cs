namespace Maple2.Server.World;

// The schedule of the time event table rows (global portals and world bosses).
public static class TimeEventRules {
    // Chosen here from one KMS2 session of a boss with a LifeTime of five minutes: the "about to disappear" notice
    // came 302 s after the spawn and the "disappeared" notice 10 s later. So the warning is sent when the LifeTime
    // has passed and the boss leaves this long after it.
    public static readonly TimeSpan BossLeaveDelay = Maple2.Model.Metadata.Constant.WorldBossLeaveDelay;

    // roll: 0..99. A row with a probability of 100 or more always runs, one of 0 never.
    public static bool Runs(int probability, int roll) {
        return probability >= 100 || roll < probability;
    }

    // The first run of a row at or after now: its StartTime while that is still ahead, else the next cycle step.
    // Null for a row without a cycle and when that run is after the EndTime.
    public static DateTime? FirstRun(DateTime startTime, TimeSpan cycleTime, DateTime endTime, DateTime now) {
        if (cycleTime <= TimeSpan.Zero || endTime < now) {
            return null;
        }

        DateTime first = startTime;
        if (first < now) {
            long steps = (now - startTime).Ticks / cycleTime.Ticks;
            first = startTime + TimeSpan.FromTicks(steps * cycleTime.Ticks);
            if (first < now) {
                first += cycleTime;
            }
        }

        return first > endTime ? null : first;
    }
}
