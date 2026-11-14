using Maple2.Model.Enum;

namespace Maple2.Server.Game.Model.Skill;

// Used to keep track and lookup pending skills
// This is a circular array and will overwrite old pending skills
public class SkillQueue {
    private const int MAX_PENDING = 3;
    // The casts a late packet may still name. The client keeps a cast's attack areas, and sends their breakable hits,
    // after the player has cast again (a magic still on its way, a lingering area), so these outlive the pending three.
    // 32 is the server's bound, not a client count: the client keeps an area as long as its cast lives.
    private const int MAX_RECENT = 32;

    private readonly SkillRecord?[] casts;
    public SkillRecord? StateSkill;
    private int index;
    private readonly SkillRecord?[] recent = new SkillRecord?[MAX_RECENT];
    private int recentIndex;

    public SkillQueue() {
        casts = new SkillRecord[MAX_PENDING];
        index = 0;
    }

    public void Add(SkillRecord cast) {
        casts[index] = cast;

        if (cast.Metadata.Property.State != ActorState.None) {
            StateSkill = cast;
        }

        index = (index + 1) % MAX_PENDING;
        recent[recentIndex] = cast;
        recentIndex = (recentIndex + 1) % MAX_RECENT;
    }

    // One of the last MAX_RECENT casts by uid, newest first.
    public SkillRecord? GetRecent(long uid) {
        for (int i = 1; i <= MAX_RECENT; i++) {
            SkillRecord? cast = recent[(recentIndex - i + MAX_RECENT) % MAX_RECENT];
            if (cast?.CastUid == uid) {
                return cast;
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
        Array.Clear(recent);
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
