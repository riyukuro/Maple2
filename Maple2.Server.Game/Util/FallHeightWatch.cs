using System.Numerics;

namespace Maple2.Server.Game.Util;

// StateFallDamage carries the height the client fell: the z of its fall top less its z as it lands (PCBase::SendFallDamage,
// pcbase.cpp:4350-4373). The top is the position where the last jump, fall or hold began (statejump.cpp:133,
// statefall.cpp:96-106, stateholddata.cpp:197; PCBase::FUN_141676060 keeps the first one, pcbase.cpp:3644-3653) and is
// cleared by each send, a glide, a warp and the end of a jump or fall that does not land (statejump.cpp:156,
// statefall.cpp:140, stateglide.cpp:205, mypc.cpp:2749, 2806). Both points were the player's (or the ride's) positions,
// which the client reports in UserSync and RideSync records.
//
// Server rule: the height should be at most the highest z less the lowest z reported since the last StateFallDamage, plus
// a slack for the positions between two records. The records holding the top and the landing may come after the packet
// (the client sends its records in batches), so a height is checked once a grace has passed, against every record up to
// then; those records start the next window too. Never cleared on a field change: a wider window allows more, never less.
public sealed class FallHeightWatch {
    public const float Slack = 300f;
    public const long GraceMs = 2000;

    private readonly object mutex = new();
    private float top = float.NegativeInfinity;
    private float bottom = float.PositiveInfinity;
    private Pending? pending;

    private sealed class Pending(float height, float top, float bottom, long due) {
        public readonly float Height = height;
        public float Top = top;
        public float Bottom = bottom;
        public readonly long Due = due;
    }

    // A height found too great once its grace is over: the height, and what the records allowed.
    public readonly record struct Excess(float Height, float Allowed);

    // A reported position. Returns the pending height's result once its grace is over.
    public Excess? Seen(Vector3 position, long now) {
        lock (mutex) {
            Add(position.Z);
            return pending != null && now >= pending.Due ? Settle() : null;
        }
    }

    private void Add(float z) {
        if (!float.IsFinite(z)) {
            return;
        }
        top = Math.Max(top, z);
        bottom = Math.Min(bottom, z);
        if (pending != null) {
            pending.Top = Math.Max(pending.Top, z);
            pending.Bottom = Math.Min(pending.Bottom, z);
        }
    }

    // A StateFallDamage height, with the player's position as the server has it. Returns the result of a height still
    // pending from before (it is settled with what was seen so far).
    public Excess? Fell(float height, Vector3 position, long now) {
        lock (mutex) {
            Excess? earlier = pending != null ? Settle() : null;
            if (float.IsFinite(height)) {
                pending = new Pending(height, top, bottom, now + GraceMs);
            }
            top = float.NegativeInfinity;
            bottom = float.PositiveInfinity;
            Add(position.Z);
            return earlier;
        }
    }

    private Excess? Settle() {
        Pending done = pending!;
        pending = null;
        // No record at all: nothing to measure by.
        if (float.IsNegativeInfinity(done.Top)) {
            return null;
        }
        float allowed = done.Top - done.Bottom + Slack;
        return done.Height > allowed ? new Excess(done.Height, allowed) : null;
    }
}
