namespace Maple2.Server.Game.Util;

// Whether the timing checks on a player's casts refuse or only log: the motion and cooldown checks of a cast
// (SkillCastRules), and the hits per attack and the held-skill iteration timing of its attack points
// (SkillHitBudget). The other cast checks (tree, positions, packet order) always refuse.
public enum CastCheckMode {
    LogOnly,
    Enforce,
}

public static class CastCheckSetting {
    // Environment variable: "enforce" refuses; anything else, or unset, only logs what would be refused.
    public const string Option = "MS2_CAST_CHECKS";

    public static readonly CastCheckMode Mode = Parse(Environment.GetEnvironmentVariable(Option));

    public static bool Enforce => Mode == CastCheckMode.Enforce;

    public static CastCheckMode Parse(string? value) {
        return string.Equals(value?.Trim(), "enforce", StringComparison.OrdinalIgnoreCase) ? CastCheckMode.Enforce : CastCheckMode.LogOnly;
    }
}

// A session's allowance for packets that arrive closer together than the client made them (a stall of the link
// or of the client, after which the held-up packets come at once). The time a cast arrives too early by is drawn
// from it, so one stall passes, and a client that keeps sending early is held to the arrival times once it is
// empty. Every number here is chosen here.
public sealed class LagBudget {
    public const long CapacityMs = 2000;
    // 1 s comes back in 10 s.
    public const long RefillMsPerSecond = 100;

    private readonly object mutex = new();
    private long level = CapacityMs;
    private long refilledAt = long.MinValue;

    public long Level(long now) {
        lock (mutex) {
            Refill(now);
            return level;
        }
    }

    // Takes the time out of the budget; false, and nothing taken, when it holds less.
    public bool TryDraw(long ms, long now) {
        lock (mutex) {
            Refill(now);
            if (ms > level) {
                return false;
            }
            level -= Math.Max(0, ms);
            return true;
        }
    }

    private void Refill(long now) {
        if (refilledAt == long.MinValue || now < refilledAt) {
            refilledAt = now;
            return;
        }
        long gained = (now - refilledAt) * RefillMsPerSecond / 1000;
        if (gained <= 0) {
            return;
        }
        // The time that made no whole millisecond is kept for the next refill.
        refilledAt += gained * 1000 / RefillMsPerSecond;
        if (level + gained >= CapacityMs) {
            level = CapacityMs;
            refilledAt = now;
        } else {
            level += gained;
        }
    }
}

// Whether the ticks a client sends advance faster than the server's time. The offset (the server's time passed
// minus the client's ticks passed) of an honest client stays where it is or rises when a packet is late; it falls
// only as far as the client's clock is fast. The lowest offset seen is kept, and follows a fall of up to the bound
// in each window; a tick whose offset lies more than the bound under it is a fast clock for the next window.
// A tick that lies more than backMs before the last one counts the same way (clock trouble): a time sync answer
// that was held up moves an honest client's ticks back by no more than the time it was held.
// Start gives the tick the server sent with its first time sync and the time it sent it at: the client sets its
// ticks to that number when it arrives, so an honest client's offset is never under 0 from there, however late
// its first tick comes. Without Start the first tick taken stands in for it.
public sealed class ClockRate {
    public const long WindowMs = 30000;
    // Chosen here. In KMS2 sessions the offset of a cast tick falls at most 341 ms under the lowest one kept, and
    // that of a position sync's tick at most 1,080 ms.
    public const long CastBoundMs = 1000;
    public const long SyncBoundMs = 2000;
    // The first ticks of a session are not measured: in KMS2 the ticks of the first position syncs jump ahead by
    // up to 9.7 s within the first 5 s.
    public const long SettleMs = 5000;
    // Chosen here, the size of the LagBudget: no KMS2 cast tick lies before the one before it.
    public const long CastBackMs = LagBudget.CapacityMs;

    private readonly object mutex = new();
    private readonly long boundMs;
    private readonly long backMs;
    private bool started;
    private int lastTick;
    private long lastNow;
    private long offset;
    private long floor;
    private long windowMin;
    private long windowEnd;
    private long settledAt;
    private long fastUntil = long.MinValue;

    public ClockRate(long boundMs, long backMs = long.MaxValue) {
        this.boundMs = boundMs;
        this.backMs = backMs;
    }

    // The session's start: the tick sent to the client and the server's time then. Only the first call counts.
    public void Start(int tick, long now) {
        lock (mutex) {
            Begin(tick, now);
        }
    }

    private bool Begin(int tick, long now) {
        if (started) {
            return false;
        }
        started = true;
        lastTick = tick;
        lastNow = now;
        windowEnd = now + WindowMs;
        settledAt = now + SettleMs;
        return true;
    }

    public bool IsFast(long now) {
        lock (mutex) {
            return now < fastUntil;
        }
    }

    // Takes a tick the client sent and the server's time at its arrival. True when this tick starts a fast time
    // (once for the time; to be logged).
    public bool Add(int tick, long now) {
        lock (mutex) {
            if (Begin(tick, now)) {
                return false;
            }
            long step = unchecked(tick - lastTick);
            bool back = step < -backMs;
            offset += now - lastNow - step;
            lastTick = tick;
            lastNow = now;
            if (now >= windowEnd) {
                floor = Math.Min(floor, windowMin);
                windowMin = offset;
                windowEnd = now + WindowMs;
            }
            windowMin = Math.Min(windowMin, offset);
            if (now < settledAt) {
                floor = Math.Min(floor, offset);
                return false;
            }
            if (!back) {
                if (offset >= floor - boundMs) {
                    return false;
                }
                // Counted on from here, with a new window: only a clock that stays fast is found again.
                floor = offset;
                windowMin = offset;
                windowEnd = now + WindowMs;
            }
            bool wasFast = now < fastUntil;
            fastUntil = now + WindowMs;
            return !wasFast;
        }
    }
}

// Holds a log line that can come with every packet to one per EveryMs (chosen here).
public sealed class LogGate {
    public const long EveryMs = 10000;

    private readonly object mutex = new();
    private int unlogged;
    private long nextLog = long.MinValue;

    // Counts one; returns how many to log now (those since the last line), 0 when a line was logged less than
    // EveryMs ago.
    public int Count(long now) {
        lock (mutex) {
            unlogged++;
            if (now < nextLog) {
                return 0;
            }
            nextLog = now + EveryMs;
            int count = unlogged;
            unlogged = 0;
            return count;
        }
    }
}
