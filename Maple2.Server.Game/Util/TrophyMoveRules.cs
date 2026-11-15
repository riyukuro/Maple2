namespace Maple2.Server.Game.Util;

/// <summary>
/// Distance counted for movement trophies and quests (run, swim, climb, glide, riding, crawl, fall). KMS2 counts one
/// for each 100 game units moved in the state and sends the counts once five or more are due (steps of 5, at times
/// 6 to 9, about 500 units each for glide, run and riding in the KMS2 captures).
/// </summary>
public static class TrophyMoveRules {
    public const float UnitDistance = 100f;
    public const int MinStep = 5;

    /// <summary>
    /// Adds a move to a state's distance and returns the counts to report: 0 until five are due, then every whole
    /// count; the rest of a count is kept.
    /// </summary>
    public static int Counts(ref float accumulated, float distance) {
        if (distance > 0 && float.IsFinite(distance)) {
            accumulated += distance;
        }
        int counts = (int) Math.Min((double) accumulated / UnitDistance, int.MaxValue);
        if (counts < MinStep) {
            return 0;
        }
        accumulated -= counts * UnitDistance;
        return counts;
    }
}

/// <summary>
/// How much of a player's synced moves counts for TrophyMoveRules. Chosen here: a move counts as far as the fastest
/// honest move goes in the time since the last one, with up to HeldMs of that time kept for positions that arrive
/// together; the jump of a move the server makes itself (a portal, the return from out of the map, a trigger) does
/// not count, nor do the client's positions for MoveRules.ServerMoveGraceMs after it, nor its first position after it
/// however late (the server does not take the place it sends the player to as the player's position). Locked: the server's own moves
/// can come from another thread than the syncs.
/// </summary>
public sealed class TrophyMoveBudget {
    // The time of fastest movement kept, so positions held up and sent together count in full.
    public const long HeldMs = 2000;
    // A fall goes faster than a move on the ground, and its height counts.
    public const float FallFactor = 2f;

    private readonly object mutex = new();
    private float available = float.NaN;
    private long last;
    private long graceUntil = long.MinValue;
    private bool skipNext;

    public void ServerMoved(long now) {
        lock (mutex) {
            graceUntil = now + MoveRules.ServerMoveGraceMs;
            skipNext = true;
        }
    }

    /// <summary>
    /// Returns the part of a move that counts. perSecond: the farthest the fastest honest move goes in a second
    /// (MoveRules.MaxDistance on foot, RideSpeed.MaxDistance on a mount).
    /// </summary>
    public float Take(float distance, long now, float perSecond) {
        lock (mutex) {
            float held = perSecond * HeldMs / 1000f;
            available = float.IsNaN(available) ? held
                : MathF.Min(held, available + perSecond * Math.Clamp(now - last, 0, HeldMs) / 1000f);
            last = now;
            if (now < graceUntil || skipNext || !(distance > 0) || !float.IsFinite(distance)) {
                skipNext = false;
                return 0f;
            }
            float taken = MathF.Min(distance, available);
            available -= taken;
            return taken;
        }
    }
}
