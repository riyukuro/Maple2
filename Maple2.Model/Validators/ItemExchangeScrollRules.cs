using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
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

    // An upgrade row (type 2) takes one target item, its requireEquip, and gives the reward in its place. The
    // client shows the row's requireEquip as the item to place (s_itemslot_exchange_upgrade_notify) and its
    // maxGrade as the highest enchant level taken (s_itemslot_exchange_upgrade_warning,
    // uiitemexchangescrolldialog.cpp:655-660); its answers are 2 (upgrade_invalid, "this item cannot be fused"),
    // 5 (grade_invalid, the enchant level is too high) and 6 (lockstate_item) (FUN_140f55670). The client checks
    // none of them itself, so the order is chosen here: the item, then its lock, then its enchant level. A
    // requireEquip rank of 0 or less takes any rarity (chosen here; every row names one).
    public static ItemExchangeScrollError? UpgradeRefusal(ItemExchangeScrollMetadata scroll, Item? target) {
        ItemComponent? equip = scroll.RequireEquip;
        if (target == null || equip == null || target.Id != equip.ItemId || (equip.Rarity > 0 && target.Rarity != equip.Rarity)) {
            return ItemExchangeScrollError.s_itemslot_exchange_upgrade_invalid;
        }
        if (target.IsLockedOrUnlocking()) {
            return ItemExchangeScrollError.s_itemslot_exchange_lockstate_item;
        }
        if ((target.Enchant?.Enchants ?? 0) > scroll.MaxGrade) {
            return ItemExchangeScrollError.s_itemslot_exchange_grade_invalid;
        }
        return null;
    }
}
