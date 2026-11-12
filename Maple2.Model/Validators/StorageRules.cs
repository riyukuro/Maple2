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

    // Slots needed for the pieces left after the existing stacks took what they could, at slotMax per slot.
    public static int SlotsNeeded(int notStackable, int slotMax) {
        if (notStackable <= 0) {
            return 0;
        }
        int perSlot = Math.Max(1, slotMax);
        return (notStackable + perSlot - 1) / perSlot;
    }

    // The mesos a storage holds at most: the client refuses a deposit that takes it over this itself (its text
    // 0x2b3) and writes the same number into s_store_err_deposit_max_money.
    public const long MaxMesos = 2_000_000_000;

    // The client's input boxes stop at the constants StoreDepositMax and StoreWithdrawMax per request
    // (0 = the constant is not set).
    public static bool IsRequestAmount(long amount, long requestMax) {
        return requestMax <= 0 || amount <= requestMax;
    }

    // The storage starts with 36 slots and StoreExpandMaxSlotCount is the most it gains: the client offers the
    // expansion while its slot count is below the constant + 36.
    public static bool CanExpand(int expanded, int row, int expandMax) {
        return expanded + row <= expandMax;
    }

    // A moveDisable item in the storage leaves only with the character that stored it (CItem FUN_14052a580:
    // no character stored with it, or the asking one).
    public static bool CanTakeOut(long storedByCharacterId, long characterId) {
        return storedByCharacterId == 0 || storedByCharacterId == characterId;
    }
}
