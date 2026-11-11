using Maple2.Model.Game;
using Maple2.Model.Metadata;

namespace Maple2.Model.Validators;

// Home maids: a furnishing with <maid maidID> places the maid of that row of table/maidproperty.xml.
public static class MaidRules {
    // The maid id of a cube that places a maid, 0 for other cubes and for a planner plot (its cubes are a preview).
    public static int MaidId(ItemMetadata metadata, bool planner) {
        return planner ? 0 : metadata.Housing?.MaidId ?? 0;
    }

    // The maid record of a maid cube placed at now (epoch seconds): affinity level 1, mood 0 (a new CMaid's
    // values), hired for PeriodOfMaidEmployment days.
    public static Maid Hire(PlotCube cube, int maidId, long ownerId, long now, int periodDays) {
        return new Maid {
            Uid = cube.Id,
            OwnerId = ownerId,
            ItemId = cube.ItemId,
            MaidId = maidId,
            CreationTime = now,
            ExpiryTime = now + (long) Math.Max(periodDays, 0) * 24 * 60 * 60,
        };
    }

    // A furnishing storage entry an added furnishing may join. A maid furnishing never stacks: its maid is keyed by
    // the storage item uid while it is stored, so each maid item keeps an entry of its own. It may take an emptied
    // entry (amount 0, kept to reuse its uid): the maid of an emptied entry moved to the cube placed from it.
    public static bool CanJoin(Item stored, int itemId, string? templateUrl, bool maidItem) {
        return stored.Id == itemId && stored.Template?.Url == templateUrl && (!maidItem || stored.Amount == 0);
    }

    // The maids (by cube uid) whose cube is not among cubeIds, the cubes now on the field's plots.
    public static List<long> Orphans(IEnumerable<long> maidCubes, IEnumerable<long> cubeIds) {
        var placed = new HashSet<long>(cubeIds);
        return maidCubes.Where(uid => !placed.Contains(uid)).ToList();
    }

    // maid_auth: the talker's account owns the maid (null: the npc is not a placed maid).
    public static bool IsOwner(Maid? maid, long accountId) => maid != null && maid.OwnerId == accountId;

    // CMaid::GetMoodGrade: 0 up to mood 30, 1 up to 70, else 2 (the recipe lead time of the mood).
    public static int MoodGrade(int mood) => mood <= 30 ? 0 : mood <= 70 ? 1 : 2;

    // CMaid::AddMood: the mood stays in 0..100; true when it changed.
    public static bool AddMood(Maid maid, int delta) {
        int old = maid.Mood;
        maid.Mood = Math.Clamp(maid.Mood + delta, 0, 100);
        return maid.Mood != old;
    }

    // The top affinity level: min(MaidAffinityMax, the maidexp row count), the row count when ignoreLimit is set
    // (CMaid::AddLevel, IsAffinityLevelAtMax).
    public static int AffinityLimit(int affinityMax, int maxLevel, bool ignoreLimit = false) {
        return ignoreLimit ? maxLevel : Math.Min(affinityMax, maxLevel);
    }

    public static bool IsAffinityLevelAtMax(Maid maid, int affinityMax, int maxLevel) {
        return AffinityLimit(affinityMax, maxLevel) <= maid.Level;
    }

    // CMaid::AddLevel: the level moves by delta within 0..limit; at the limit the exp is cleared.
    public static void AddLevel(Maid maid, int delta, int affinityMax, int maxLevel, bool ignoreLimit = false) {
        int limit = AffinityLimit(affinityMax, maxLevel, ignoreLimit);
        maid.Level += delta;
        if (limit <= maid.Level) {
            maid.Exp = 0;
        }
        maid.Level = Math.Max(0, Math.Min(limit, maid.Level));
    }

    // CMaid::ResetLevel.
    public static void ResetLevel(Maid maid) {
        maid.Exp = 0;
        maid.Level = 1;
    }

    // Affinity exp gained. The maid dialog shows exp / maidexp[level] as the gauge of the level, so the exp of a level
    // is what it takes to reach the next one; the gain carries over and stops at the top level (where the exp is 0,
    // as in AddLevel). Returns true when the level changed.
    public static bool AddExp(Maid maid, int delta, IReadOnlyDictionary<int, int> exps, int affinityMax) {
        int limit = AffinityLimit(affinityMax, exps.Count);
        int oldLevel = maid.Level;
        if (maid.Level >= limit) {
            maid.Exp = 0;
            return false;
        }
        maid.Exp = Math.Max(0, maid.Exp + delta);
        while (maid.Level < limit && exps.TryGetValue(maid.Level, out int need) && need > 0 && maid.Exp >= need) {
            maid.Exp -= need;
            maid.Level++;
        }
        if (maid.Level >= limit) {
            maid.Exp = 0;
        }
        return maid.Level != oldLevel;
    }

    // The profile of the maid dialog's save (Maid mode 4): only the owner, and only while the profile has no name
    // (the dialog's "editable" outside debug mode).
    public static bool CanEditProfile(Maid maid, long accountId, bool debug) {
        return maid.OwnerId == accountId && (debug || string.IsNullOrEmpty(maid.Profile.Name));
    }

    // The 13 texts of a profile, for the ban word check.
    public static IEnumerable<string> ProfileTexts(MaidProfile profile) {
        return [profile.Title, profile.Name, profile.BirthDay, profile.Constellation, profile.Height, profile.Weight, profile.Specialty,
            profile.SpecialDetail, profile.Like, profile.Hate, profile.Hobby, profile.OwnerName, profile.BelongTo];
    }

    // The contract end after a salary payment of periodDays: from the current end, or from now once it has passed.
    public static long PaidUntil(long expiryTime, long now, int periodDays) {
        return Math.Max(expiryTime, now) + (long) Math.Max(periodDays, 0) * 24 * 60 * 60;
    }

    // A maid change scheduled on the field thread runs only while maid is still the placed maid of its uid (current,
    // read again on that thread): a maid removed or placed again meanwhile is left alone.
    public static bool StillPlaced(Maid? current, Maid maid) => current != null && ReferenceEquals(current, maid);

    // maidPay on the field thread: charge takes the salary and says whether it could; only a taken salary extends
    // the contract. The caller runs it after StillPlaced, so no salary is taken for a gone maid.
    public static bool Pay(Maid maid, Func<bool> charge, long now, int periodDays) {
        if (!charge()) {
            return false;
        }
        maid.ExpiryTime = PaidUntil(maid.ExpiryTime, now, periodDays);
        return true;
    }

    // The maid dialog's debug buttons (Maid modes 5-9; 10 is Expiry). Mood moves by moodStep.
    public enum DebugCommand { AffinityGrade = 5, AffinityUp = 6, AffinityReset = 7, MoodUp = 8, MoodDown = 9, Expiry = 10 }

    // Applies a debug button to maid: Expiry moves the contract end by seconds, earlier for flag 0, later for 1.
    public static void ApplyDebug(Maid maid, DebugCommand command, byte flag, long seconds, IReadOnlyDictionary<int, int> exps,
                                  int affinityMax, int moodStep) {
        switch (command) {
            case DebugCommand.AffinityGrade:
                AddLevel(maid, 1, affinityMax, exps.Count);
                break;
            case DebugCommand.AffinityUp:
                AddExp(maid, exps.GetValueOrDefault(maid.Level), exps, affinityMax);
                break;
            case DebugCommand.AffinityReset:
                ResetLevel(maid);
                break;
            case DebugCommand.MoodUp:
                AddMood(maid, moodStep);
                break;
            case DebugCommand.MoodDown:
                AddMood(maid, -moodStep);
                break;
            case DebugCommand.Expiry:
                maid.ExpiryTime += flag == 0 ? -Math.Max(0, seconds) : Math.Max(0, seconds);
                break;
        }
    }
}
