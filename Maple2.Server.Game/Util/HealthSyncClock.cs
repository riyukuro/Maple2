namespace Maple2.Server.Game.Util;

// Paces the health a player's hits send to the world. The first change after a quiet interval goes at once, later
// ones inside the interval are held and the latest health goes when the interval ends; a forced change (death) goes
// at once. Hits come from the field thread, heals and costs also from a session thread, so the state is locked.
public sealed class HealthSyncClock {
    public const long IntervalTick = 500;

    private readonly object mutex = new();
    private long lastSentTick = -IntervalTick;
    private bool pending;
    public bool Pending {
        get {
            lock (mutex) {
                return pending;
            }
        }
    }

    // A health change at this tick: true when it is to be sent now, false when it is held.
    public bool Changed(long tick, bool force = false) {
        lock (mutex) {
            if (force || tick - lastSentTick >= IntervalTick) {
                lastSentTick = tick;
                pending = false;
                return true;
            }
            pending = true;
            return false;
        }
    }

    // Each field tick: true when a held change is due.
    public bool Due(long tick) {
        lock (mutex) {
            if (!pending || tick - lastSentTick < IntervalTick) {
                return false;
            }
            lastSentTick = tick;
            pending = false;
            return true;
        }
    }

    // The player leaves the field: true when a held change is to go now, as nothing sends it later.
    public bool Flush(long tick) {
        lock (mutex) {
            if (!pending) {
                return false;
            }
            lastSentTick = tick;
            pending = false;
            return true;
        }
    }

    // The health went out by another way (a heal, regen, a cost): nothing is held any more.
    public void Sent(long tick) {
        lock (mutex) {
            lastSentTick = tick;
            pending = false;
        }
    }
}
