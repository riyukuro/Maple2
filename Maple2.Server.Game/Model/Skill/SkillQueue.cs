using Maple2.Model.Enum;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Game.Model.Skill;

// Used to keep track and lookup pending skills
// This is a circular array and will overwrite old pending skills
public class SkillQueue {
    private const int MAX_PENDING = 3;
    // The casts a late packet may still name. The client keeps a cast's attack areas, and sends their breakable hits,
    // after the player has cast again (a magic still on its way, a lingering area), so these outlive the pending three:
    // each is kept for its keep time (SkillHitRules.CastKeepMs) after the later of its add and its hit window, however
    // many casts come after it. MAX_RECENT only bounds the server's memory, far above any cast rate over a keep time
    // (at most 65 s): at that count the oldest is dropped.
    private const int MAX_RECENT = 4096;

    private readonly SkillRecord?[] casts;
    public SkillRecord? StateSkill;
    private int index;
    private readonly LinkedList<(SkillRecord Cast, long AddTick, long KeepMs)> recent = [];

    public SkillQueue() {
        casts = new SkillRecord[MAX_PENDING];
        index = 0;
    }

    // tick: the field tick of the add. keepMs: how long the cast may still be named (SkillHitRules.CastKeepMs).
    public void Add(SkillRecord cast, long tick, long keepMs = SkillHitRules.HitWindowMs) {
        casts[index] = cast;

        if (cast.Metadata.Property.State != ActorState.None) {
            StateSkill = cast;
        }

        index = (index + 1) % MAX_PENDING;
        while (recent.First is { } oldest && (!Live(oldest.Value, tick) || recent.Count >= MAX_RECENT)) {
            recent.RemoveFirst();
        }
        recent.AddLast((cast, tick, keepMs));
    }

    private static bool Live((SkillRecord Cast, long AddTick, long KeepMs) entry, long tick) {
        return tick - Math.Max(entry.AddTick, entry.Cast.Hits.WindowTick) <= entry.KeepMs;
    }

    // A cast still within its keep time at tick, by uid, newest first.
    public SkillRecord? GetRecent(long uid, long tick) {
        for (LinkedListNode<(SkillRecord Cast, long AddTick, long KeepMs)>? node = recent.Last; node != null; node = node.Previous) {
            if (node.Value.Cast.CastUid == uid) {
                return Live(node.Value, tick) ? node.Value.Cast : null;
            }
        }
        return null;
    }

    public SkillRecord? Get(long uid) {
        for (int i = 0; i < MAX_PENDING; i++) {
            if (casts[i]?.CastUid == uid) {
                return casts[i];
            }
        }

        return null;
    }

    public void Remove(long uid) {
        for (int i = 0; i < MAX_PENDING; i++) {
            if (casts[i]?.CastUid != uid) {
                continue;
            }

            casts[i] = null;
            return;
        }
    }

    public void Clear() {
        for (int i = 0; i < MAX_PENDING; i++) {
            casts[i] = null;
        }
        recent.Clear();
    }

    /// <summary>
    /// Returns the most recently added non-null SkillRecord, or null if none exist.
    /// </summary>
    public SkillRecord? GetMostRecent() {
        // Walk backwards from the last written index
        for (int i = 0; i < MAX_PENDING; i++) {
            int idx = (index - 1 - i + MAX_PENDING) % MAX_PENDING;
            if (casts[idx] != null) {
                return casts[idx];
            }
        }
        return null;
    }
}
