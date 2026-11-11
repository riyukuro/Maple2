namespace Maple2.Model.Validators;

// Tombstone: i32 object id of the dead player, i32 hit count.
public static class TombstoneRules {
    // The client reports one hit per packet and never for the tombstone its own player owns.
    public static bool IsHit(int hits, int ownerObjectId, int hitterObjectId) {
        return hits == 1 && ownerObjectId != hitterObjectId;
    }

    public static byte AfterHit(byte hitsRemaining) {
        return hitsRemaining > 0 ? (byte) (hitsRemaining - 1) : (byte) 0;
    }
}
