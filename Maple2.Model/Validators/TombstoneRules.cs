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
    // Chosen here: how much ahead of the client's schedule (one hit per interval since the state entry) hits may run.
    public const long PET_HIT_SLACK_MS = 500;

    // The pet's revive state holds while its owner lies dead with a tombstone.
    public static bool PetHolds(bool ownerDead, bool hasTombstone) {
        return ownerDead && hasTombstone;
    }

    public static bool PetHelps(bool ownerDead, bool hasTombstone, DeathState state, bool revivalBuff) {
        return PetHolds(ownerDead, hasTombstone) && state != DeathState.Metal && !revivalBuff;
    }

    // The client's revive state sends one hit each time its 3000 ms run out, the first 3000 ms after it enters the
    // state (which is after the server put the pet in it). Hits are paced against that schedule, not against the last
    // arrival: a late packet followed by one on time (network jitter) both count, and only more hits than the time
    // since the entry (plus the slack) allows are refused.
    public static bool PetHitDue(long tick, long enteredTick, int hitsTaken) {
        long elapsed = tick - enteredTick + PET_HIT_SLACK_MS;
        return elapsed >= 0 && hitsTaken < elapsed / PET_HIT_INTERVAL_MS;
    }

    // The death penalty count makes the tombstone dark only while it runs: the client clears its count when the server
    // tick reaches the end tick (PC::FUN_141661ac0, from the PC update, pc.cpp:4470; an end tick of 0 is never cleared
    // there), and its death (PC::vfunc_137) darkens the tombstone only for a count above 0. The server's count stays
    // until the next field entry or penalty update, so the count alone would darken a tombstone the client does not.
    public static bool PenaltyRuns(int deathCount, long penaltyEndTick, long tick) {
        return deathCount > 0 && (penaltyEndTick == 0 || tick < penaltyEndTick);
    }
}
