using System.Numerics;

namespace Maple2.Server.Game.Util;

// StateFallDamage carries the height the client fell: the z of its fall top less its z as it lands (PCBase::SendFallDamage,
// pcbase.cpp:4350-4373). The top is the position where the last jump, fall or hold began (statejump.cpp:133,
// statefall.cpp:96-106, stateholddata.cpp:197; PCBase::FUN_141676060 keeps the first one, pcbase.cpp:3644-3653), for a rider
// getting off mid-air the ride's own fall top (pcbase.cpp:3414, FUN_141676020 at 2114), and is cleared by each send, a glide,
// a warp and the end of a jump or fall that does not land (statejump.cpp:156, statefall.cpp:140, stateglide.cpp:205,
// mypc.cpp:2749, 2806). Both points were the player's (or the ride's) positions, which the client reports in UserSync and
// RideSync records.
//
// Server rule: the height should be at most the highest z less the lowest z reported since the last StateFallDamage, plus
// a slack for the positions between two records. The client sends its records in batches once their time adds up
// (state.cpp:411-423) and the report at once (mypc.cpp:2947-2951), so the records holding the top and the landing may come
// after the report, a hitch's worth late. A height is settled at the first record packet after one that came when its
// grace was over, against every record up to then; those records start the next window too, and they count for every
// report still pending. Never cleared on a field change: a wider window allows more, never less. A report after the session
// rode as a passenger is not tested: a passenger's client sends no records of its own while seated, and its fall top may
// be a mid-air position no record holds (statefall.cpp:96-106).
public sealed class FallHeightWatch {
    public const float Slack = 300f;
    public const long GraceMs = 2000;
    // Reports pending at once; an older one is dropped untested.
    public const int MaxPending = 8;

    private readonly long graceMs;
    private readonly object mutex = new();
    private float top = float.NegativeInfinity;
    private float bottom = float.PositiveInfinity;
    private bool passenger;
    private readonly List<Pending> pending = [];

    public FallHeightWatch(long graceMs = GraceMs) {
        this.graceMs = graceMs;
    }

    private sealed class Pending(float height, float top, float bottom, long due) {
        public readonly float Height = height;
        public float Top = top;
        public float Bottom = bottom;
        public readonly long Due = due;
        // A record packet came after the grace: the next one settles the height.
        public bool Ripe;
    }

    // A height found too great once settled: the height, and what the records allowed.
    public readonly record struct Excess(float Height, float Allowed);

    // The positions of one UserSync or RideSync packet. Returns the heights settled too great.
    public IReadOnlyList<Excess> Seen(IEnumerable<Vector3> positions, long now) {
        lock (mutex) {
            foreach (Vector3 position in positions) {
                Add(position.Z);
            }
            List<Excess>? found = null;
            for (int i = pending.Count - 1; i >= 0; i--) {
                Pending entry = pending[i];
                if (!entry.Ripe) {
                    entry.Ripe = now >= entry.Due;
                    continue;
                }
                pending.RemoveAt(i);
                // No z at all: nothing to measure by.
                if (float.IsNegativeInfinity(entry.Top)) {
                    continue;
                }
                float allowed = entry.Top - entry.Bottom + Slack;
                if (entry.Height > allowed) {
                    (found ??= []).Add(new Excess(entry.Height, allowed));
                }
            }
            return found ?? (IReadOnlyList<Excess>) [];
        }
    }

    private void Add(float z) {
        if (!float.IsFinite(z)) {
            return;
        }
        top = Math.Max(top, z);
        bottom = Math.Min(bottom, z);
        foreach (Pending entry in pending) {
            entry.Top = Math.Max(entry.Top, z);
            entry.Bottom = Math.Min(entry.Bottom, z);
        }
    }

    // The session took a seat on another player's ride: its next report is not tested.
    public void Passenger() {
        lock (mutex) {
            passenger = true;
        }
    }

    // A StateFallDamage height, with the player's position as the server has it (counted as a record).
    public void Fell(float height, Vector3 position, long now) {
        lock (mutex) {
            if (float.IsFinite(height) && !passenger) {
                if (pending.Count >= MaxPending) {
                    pending.RemoveAt(0);
                }
                pending.Add(new Pending(height, top, bottom, now + graceMs));
            }
            passenger = false;
            top = float.NegativeInfinity;
            bottom = float.PositiveInfinity;
            Add(position.Z);
        }
    }
}
