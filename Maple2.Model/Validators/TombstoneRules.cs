using Maple2.Model.Enum;

namespace Maple2.Model.Validators;

// Tombstone: i32 object id of the dead player, i32 hit count.
public static class TombstoneRules {
    // The client reports one hit per packet and never for the tombstone its own player owns.
    public static bool IsHit(int hits, int ownerObjectId, int hitterObjectId) {
        return hits == 1 && ownerObjectId != hitterObjectId;
    }

    // A broken tombstone takes no more hits: a repeat packet must not count for hit_tombstone.
    public static bool CanHit(byte hitsRemaining) {
        return hitsRemaining > 0;
    }

    // timeResetDead when the constants have no row for it. Chosen here: the value of the KMS data.
    public const int DEFAULT_RESET_SECONDS = 180;

    // A row no later death can add to.
    public static bool RowExpired(long lastDeathMs, long nowMs, int resetSeconds) {
        return nowMs - lastDeathMs >= resetSeconds * 1000L;
    }

    // Deaths in a row: a death within timeResetDead seconds of the one before adds to the row, a later one
    // starts a new row. The time runs from death to death, a revive in between does not move it.
    public static int DeathStreak(int previousStreak, long previousDeathMs, long nowMs, int resetSeconds) {
        if (previousStreak <= 0) {
            return 1;
        }
        long gap = nowMs - previousDeathMs;
        return gap >= 0 && gap < resetSeconds * 1000L ? previousStreak + 1 : 1;
    }

    // hitPerDeadCount hits for each death in the row, at most maxDeadCount deaths counted.
    public static byte TotalHits(int deathStreak, int hitPerDeadCount, int maxDeadCount) {
        int deaths = Math.Clamp(deathStreak, 1, Math.Max(maxDeadCount, 1));
        return (byte) Math.Clamp(deaths * hitPerDeadCount, 0, byte.MaxValue);
    }

    public static byte AfterHit(byte hitsRemaining) {
        return hitsRemaining > 0 ? (byte) (hitsRemaining - 1) : (byte) 0;
    }

    // A summoned pet helps its dead owner up: the client's StateSummonRevive (statesummon.cpp:787) runs 3000 ms and
    // then, when the owner is dead and PC::IsRevivalBlocked (pc.cpp:2009) is false, sends Revival 3 with the pet's
    // object id. IsRevivalBlocked: a death count (RevivalConfirm), the dark tombstone or a buff with
    // disableRevivalHere; the first two are DeathState.Metal here.
    public const long PET_HIT_INTERVAL_MS = 3000;
    // Chosen here: how much sooner than the client's interval a pet hit may come.
    public const long PET_HIT_SLACK_MS = 500;

    // The pet's revive state holds while its owner lies dead with a tombstone.
    public static bool PetHolds(bool ownerDead, bool hasTombstone) {
        return ownerDead && hasTombstone;
    }

    public static bool PetHelps(bool ownerDead, bool hasTombstone, DeathState state, bool revivalBuff) {
        return PetHolds(ownerDead, hasTombstone) && state != DeathState.Metal && !revivalBuff;
    }

    // The client's revive state sends one hit after its 3000 ms run, so a hit must come at least the interval (less the
    // slack) after the later of the pet's entry into the state and its last hit (lastTick 0: no hit yet).
    public static bool PetHitTooSoon(long tick, long enteredTick, long lastTick) {
        return tick - Math.Max(enteredTick, lastTick) < PET_HIT_INTERVAL_MS - PET_HIT_SLACK_MS;
    }
}
