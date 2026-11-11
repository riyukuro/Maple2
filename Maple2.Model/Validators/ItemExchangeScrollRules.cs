using Maple2.PacketLib.Tools;

namespace Maple2.Model.Validators;

// ItemExchangeScroll mode 1 (UIItemExchangeScrollDialog::SendItemExchangeScrollMode1): the exchange of
// count sets of a scroll's ingredients for count sets of its reward.
public static class ItemExchangeScrollRules {
    // Server limit. The client refuses 0 itself and bounds the count by what the player holds.
    public const int MaxCount = 1000;

    // After the mode byte: i64 scroll uid, i64 target uid, i32 count.
    public static (long ScrollUid, long TargetUid, int Count) ReadExchange(IByteReader packet) {
        long scrollUid = packet.ReadLong();
        long targetUid = packet.ReadLong();
        int count = packet.ReadInt();
        return (scrollUid, targetUid, count);
    }

    public static bool CountAllowed(int count) {
        return count is >= 1 and <= MaxCount;
    }

    // The fee of count exchanges, or -1 when it is not a payable amount.
    public static long Fee(long mesoPerExchange, int count) {
        if (mesoPerExchange < 0 || !CountAllowed(count) || mesoPerExchange > long.MaxValue / count) {
            return -1;
        }
        return mesoPerExchange * count;
    }

    // How much to take from each stack, in order, to take total; null when the stacks hold less.
    public static int[]? TakeFromStacks(IReadOnlyList<int> stacks, long total) {
        if (total < 0) {
            return null;
        }

        int[] take = new int[stacks.Count];
        long remaining = total;
        for (int i = 0; i < stacks.Count && remaining > 0; i++) {
            take[i] = (int) Math.Min(remaining, Math.Max(0, stacks[i]));
            remaining -= take[i];
        }
        return remaining == 0 ? take : null;
    }
}
