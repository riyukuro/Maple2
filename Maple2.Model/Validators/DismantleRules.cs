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

    // The stacks the confirm can pay at most: every reward at its max, one entry per stack. A reward id
    // without a stack size (no item metadata) gives nothing and takes no room.
    public static List<(int Id, int Amount)> MaxRewardStacks(IReadOnlyDictionary<int, (int Min, int Max)> rewards,
            Func<int, int?> slotMax) {
        var stacks = new List<(int Id, int Amount)>();
        foreach ((int id, (int _, int max)) in rewards.OrderBy(entry => entry.Key)) {
            int? size = slotMax(id);
            if (size == null) {
                continue;
            }
            foreach (int stack in Stacks(max, size.Value)) {
                stacks.Add((id, stack));
            }
        }
        return stacks;
    }
}
