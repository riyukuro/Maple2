namespace Maple2.Server.Game.Util;

// The respawn timers of one region spawn: each dead mob gets a timer of its own and comes back alone, at its death
// + the npc's Dead.Time + the spawn's Cooldown, whatever the rest of its group does (cooldown 13 brings a mob back
// 18.0 s after its death with Dead.Time 5). A respawn never takes the group over its Population.
public sealed class RespawnQueue {
    private readonly List<long> due = [];

    public int Count => due.Count;

    // A mob died at tick.
    public void Died(long tick, float deadTimeSeconds, int cooldownSeconds) {
        if (cooldownSeconds <= 0) {
            return; // no respawn without a cooldown
        }
        due.Add(tick + (long) ((Math.Max(deadTimeSeconds, 0) + cooldownSeconds) * 1000));
    }

    // The number of mobs to spawn now: the timers due at tick, at most free (the group's free places). Taken timers are
    // dropped; timers past free stay (a full group has no room for them until a mob dies).
    public int Take(long tick, int free) {
        if (free <= 0 || due.Count == 0) {
            return 0;
        }
        due.Sort();
        int count = 0;
        while (count < due.Count && count < free && due[count] <= tick) {
            count++;
        }
        due.RemoveRange(0, count);
        return count;
    }
}
