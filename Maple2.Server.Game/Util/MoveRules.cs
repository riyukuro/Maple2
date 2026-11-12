using System.Numerics;

namespace Maple2.Server.Game.Util;

// A bound on how far a player's own position syncs move it. Every number here is chosen here.
public static class MoveRules {
    // The time the distance is measured over, by the tick the client sends with each position. A position stays
    // in the window for that long by its arrival as well, so ticks that run fast do not shorten it; positions
    // that were held up and arrive together leave it by their ticks for as long as the session's LagBudget for
    // moves holds the time they arrive early by.
    public const int WindowMs = 1000;
    // Walking is 350 units a second at movement speed 100. In KMS2 two positions 1 s apart lie within 1,726 units
    // (half within 346, 99 in 100 within 748, 999 in 1000 within 1,011), but for the moves the server makes
    // itself. Mounts are not told apart.
    public const float MaxDistance = 1800f;
    // After the server moves the player itself, positions are not measured for this long: the client still sends
    // from the old place for a moment.
    public const long ServerMoveGraceMs = 2000;
    // A player's fast moves are logged once in this long, with their count.
    public const long LogEveryMs = 10000;

    // The height is not counted: a fall is faster than any move on the ground.
    public static bool TooFar(Vector3 from, Vector3 to) {
        return Vector2.Distance(new Vector2(from.X, from.Y), new Vector2(to.X, to.Y)) > MaxDistance;
    }
}

// The positions a client synced within the last MoveRules.WindowMs. Locked: the server's own moves can come from
// another thread than the syncs.
public sealed class MoveWindow {
    private readonly object mutex = new();
    private long nextLog = long.MinValue;
    private int unlogged;

    private readonly Queue<(int Tick, Vector3 Position, long Now)> samples = new();
    private long granted; // the time drawn from the lag budget for the positions leaving the window now
    private long grantedAt;
    private int lastTick;
    private long graceUntil = long.MinValue;

    // The server moved the player: the positions kept are of the old place.
    public void ServerMoved(long now) {
        lock (mutex) {
            samples.Clear();
            graceUntil = now + MoveRules.ServerMoveGraceMs;
        }
    }

    // Counts a fast move; returns how many to log now (those since the last line), 0 when a line was logged less
    // than MoveRules.LogEveryMs ago.
    public int CountFast(long now) {
        lock (mutex) {
            unlogged++;
            if (now < nextLog) {
                return 0;
            }
            nextLog = now + MoveRules.LogEveryMs;
            int count = unlogged;
            unlogged = 0;
            return count;
        }
    }

    // Takes a synced position. Returns the position of up to MoveRules.WindowMs before when this one is further
    // from it than MoveRules.MaxDistance, and starts again from this one then.
    // tick: the client's tick of the position. now: the server's clock. lag: the budget for positions that arrive
    // early; with none the client's ticks alone count. clockFast: the client's ticks run fast (ClockRate), so
    // nothing is drawn and the arrivals alone count.
    public Vector3? Add(int tick, Vector3 position, long now, LagBudget? lag = null, bool clockFast = false) {
        lock (mutex) {
            return AddLocked(tick, position, now, lag, clockFast);
        }
    }

    // Whether the oldest position, out of the window by its tick, is out of it by its arrival too, or the budget
    // gives the time missing. The time drawn runs off with the server's clock: positions of one burst draw once.
    private bool Leaves(long arrived, long now, LagBudget? lag, bool clockFast) {
        long need = MoveRules.WindowMs - (now - arrived);
        if (lag == null || need <= 0) {
            return true;
        }
        long have = Math.Max(0, granted - (now - grantedAt));
        if (need <= have) {
            return true;
        }
        if (clockFast || !lag.TryDraw(need - have, now)) {
            return false;
        }
        granted = need;
        grantedAt = now;
        return true;
    }

    private Vector3? AddLocked(int tick, Vector3 position, long now, LagBudget? lag, bool clockFast) {
        if (now < graceUntil) {
            samples.Clear();
            return null;
        }
        // A tick that runs backwards starts again.
        if (samples.Count > 0 && unchecked(tick - lastTick) < 0) {
            samples.Clear();
        }
        lastTick = tick;
        while (samples.Count > 0 && unchecked(tick - samples.Peek().Tick) > MoveRules.WindowMs && Leaves(samples.Peek().Now, now, lag, clockFast)) {
            samples.Dequeue();
        }

        Vector3? from = null;
        if (samples.Count > 0 && MoveRules.TooFar(samples.Peek().Position, position)) {
            from = samples.Peek().Position;
            samples.Clear();
        }
        samples.Enqueue((tick, position, now));
        return from;
    }
}
