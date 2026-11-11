namespace Maple2.Model.Validators;

// RequestItemStorage 0x0018, modes 0 (inventory to storage) and 1 (storage to inventory):
// i64 storage id, i64 item uid, i16 slot, i32 count.
public static class StorageRules {
    // The client sends the stack's own count or the count of its input box, which is 1 to the stack.
    // A count of 0 or below fell through to "the whole stack" on the server.
    public static bool IsCount(int count, int owned) {
        return count > 0 && count <= owned;
    }

    // The client refuses to store the pet that is out (s_item_err_binditem_by_summon_pet).
    public static bool CanDeposit(long itemUid, long summonedPetUid) {
        return summonedPetUid == 0 || itemUid != summonedPetUid;
    }

    // The client refuses to store the score that is being played (s_item_err_binditem_by_playing_score).
    public static bool IsPlayedScore(long itemUid, long playedScoreUid) {
        return playedScoreUid != 0 && itemUid == playedScoreUid;
    }

    // The client looks for an inventory slot before it asks; without one the item has no place to go.
    public static bool HasRoom(int openSlots, int notStackable) {
        return openSlots > 0 || notStackable == 0;
    }
}
