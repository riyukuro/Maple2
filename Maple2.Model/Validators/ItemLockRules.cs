namespace Maple2.Model.Validators;

// RequestItemLock 0x0087 mode 1: u8 mode (0 lock, 1 unlock), i64 item uid.
// The client refuses an item that is already in the window, a locked item in lock mode and an
// item that is not locked in unlock mode.
public static class ItemLockRules {
    // CItem::CanBeItemLocked: the item data types (the item xml's property type) that take a lock.
    public static bool IsLockableType(int type) {
        return type is 1 or 5 or 9 or 11 or 12 or 13 or 21;
    }

    public static bool CanStage(bool unlock, bool owned, bool locked, bool alreadyStaged) {
        if (!owned || alreadyStaged) {
            return false;
        }
        return unlock == locked;
    }
}
