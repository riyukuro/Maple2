using System.Numerics;

namespace Maple2.Server.Game.Util;

// A bound on how far a player's own position syncs move it. Every number here is chosen here.
public static class MoveRules {
    // The time the distance is measured over, by the tick the client sends with each position.
    public const int WindowMs = 1000;
    // Walking is 350 units a second at movement speed 100. In KMS2 two positions 1 s apart lie within 1,726 units
    // (half within 346, 99 in 100 within 748, 999 in 1000 within 1,011), but for the moves the server makes
    // itself. Mounts are not told apart.
    public const float MaxDistance = 1800f;
    // After the server moves the player itself, positions are not measured for this long: the client still sends
    // from the old place for a moment.
    public const long ServerMoveGraceMs = 2000;

    // The height is not counted: a fall is faster than any move on the ground.
    public static bool TooFar(Vector3 from, Vector3 to) {
        return Vector2.Distance(new Vector2(from.X, from.Y), new Vector2(to.X, to.Y)) > MaxDistance;
    }
}

// The positions a client synced within the last MoveRules.WindowMs.
public sealed class MoveWindow {
    private readonly Queue<(int Tick, Vector3 Position)> samples = new();
    private int lastTick;
    private long graceUntil = long.MinValue;

    // The server moved the player: the positions kept are of the old place.
    public void ServerMoved(long now) {
        samples.Clear();
        graceUntil = now + MoveRules.ServerMoveGraceMs;
    }

    // Takes a synced position. Returns the position of up to MoveRules.WindowMs before when this one is further
    // from it than MoveRules.MaxDistance, and starts again from this one then.
    // tick: the client's tick of the position. now: the server's clock.
    public Vector3? Add(int tick, Vector3 position, long now) {
        if (now < graceUntil) {
            samples.Clear();
            return null;
        }
        // A tick that runs backwards starts again.
        if (samples.Count > 0 && unchecked(tick - lastTick) < 0) {
            samples.Clear();
        }
        lastTick = tick;
        while (samples.Count > 0 && unchecked(tick - samples.Peek().Tick) > MoveRules.WindowMs) {
            samples.Dequeue();
        }

        Vector3? from = null;
        if (samples.Count > 0 && MoveRules.TooFar(samples.Peek().Position, position)) {
            from = samples.Peek().Position;
            samples.Clear();
        }
        samples.Enqueue((tick, position));
        return from;
    }
}
