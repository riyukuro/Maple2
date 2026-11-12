namespace Maple2.Model.Validators;

// RequestItemLock 0x0087 mode 1: u8 mode (0 lock, 1 unlock), i64 item uid.
// The client refuses an item that is already in the window, a locked item in lock mode and an
// item that is not locked in unlock mode.
public static class ItemLockRules {
    // CItem::CanBeItemLocked: the item data types (the item xml's property type) that take a lock.
    public static bool IsLockableType(int type) {
        return type is 1 or 5 or 9 or 11 or 12 or 13 or 21;
    }

    // CItem::CanBeItemLocked: VIP items and wedding-limit items never take a lock.
    public static bool LimitAllowsLock(bool requireVip, bool requireWedding) {
        return !requireVip && !requireWedding;
    }

    // CItemLockManager::CanUseItem: a locked item, and one whose unlock wait is not over, is not sold, traded,
    // dropped or destroyed (the client asks it before each of those requests).
    public static bool IsUsable(bool isLocked, long unlockTime, long now) {
        return !isLocked && now >= unlockTime;
    }

    // InvenSlot's drop and destroy (invenslot.cpp vfunc_26): not while dead, not the pet that is out, not the
    // score that is being played, not a locked item, and a count of 1 to the stack.
    public static bool CanDrop(bool dead, bool usable, long itemUid, long summonedPetUid, long playedScoreUid) {
        if (dead || !usable) {
            return false;
        }
        return (summonedPetUid == 0 || itemUid != summonedPetUid) && (playedScoreUid == 0 || itemUid != playedScoreUid);
    }

    // UseItem (@0x140d1bd60) asks CanUseItem, which refuses an item that CItem::FUN_140528980 says is not usable: an
    // expired item, unless it is a pet (item data type 11, CItemData::IsPetItem) or a furnishing with a maid id
    // (CItemData::HasMaidId). The client shows notice 0xe1 for it and sends nothing.
    public static bool ExpiryAllowsUse(bool expired, int propertyType, int maidId) {
        return !expired || propertyType == 11 || maidId > 0;
    }

    public static bool CanStage(bool unlock, bool owned, bool locked, bool alreadyStaged) {
        if (!owned || alreadyStaged) {
            return false;
        }
        return unlock == locked;
    }
}
