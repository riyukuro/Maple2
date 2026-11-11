namespace Maple2.Model.Validators;

// ItemDismantle 0x005A mode 1: i32 slot (-1), i64 item uid, i32 count.
public static class DismantleRules {
    // The client's count box takes 1 to the stack (CItemBreakManager::OnDismantleCountMsgBox), its
    // CanBreakItem refuses a locked item and the pet that is out.
    public static bool CanStage(int count, int owned, bool locked, long itemUid, long summonedPetUid) {
        if (count <= 0 || count > owned) {
            return false;
        }
        if (locked) {
            return false;
        }
        return summonedPetUid == 0 || itemUid != summonedPetUid;
    }

    // The stacks a reward of <paramref name="total"/> pieces is given in. Each stack is one item:
    // giving the whole remainder on every pass paid a reward above the stack size more than once.
    public static IEnumerable<int> Stacks(int total, int slotMax) {
        slotMax = Math.Max(1, slotMax);
        while (total > 0) {
            int stack = Math.Min(slotMax, total);
            total -= stack;
            yield return stack;
        }
    }
}
