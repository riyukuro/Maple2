namespace Maple2.Model.Validators;

// ItemDismantle 0x005A mode 1: i32 slot (-1), i64 item uid, i32 count.
public static class DismantleRules {
    // The client's count box takes 1 to the stack (CItemBreakManager::OnDismantleCountMsgBox), its
    // CanBreakItem refuses a locked item, one still unlocking (CItem::FUN_140528e20) and the pet that is out.
    public static bool CanStage(int count, int owned, bool locked, long itemUid, long summonedPetUid) {
        if (count <= 0 || count > owned) {
            return false;
        }
        if (locked) {
            return false;
        }
        return summonedPetUid == 0 || itemUid != summonedPetUid;
    }

    // CItemBreakManager::CanBreakItem: an item from a gacha that can be dismantled (FUN_140531510) passes first;
    // any other needs CItemTransfer::CanBreak, which refuses an item whose data has no enableBreak (m_b3E) and one
    // given with the no-dismantle flag (m_b3F).
    public static bool Breakable(bool enableBreak, bool disableBreak, int gachaDismantleId) {
        return gachaDismantleId != 0 || enableBreak && !disableBreak;
    }

    // The donation box (dialog type 2, UIItemBreakDonationDialog): CItemBreakManager::TryAddItem refuses with
    // s_itembreak_invalid_donation_item (0x8f7) an item CanBreakItem refuses, and one whose data is not of type 1
    // (CItemData::FUN_14053cbc0, property type == 1) unless it has a skin type (CItem::HasSkin ->
    // CItemData::HasSkinType, skinType != 0).
    public static bool CanDonate(bool breakable, int itemType, int skinType) {
        return breakable && (itemType == 1 || skinType != 0);
    }

    // The socket part of an outfit's dismantle reward (ItemBreakCommon 0x140530c90): each opened socket returns
    // percent of the unlock ingredient's count, at least 1, and nothing at 0 percent.
    public static int SkinSocketRefund(int unlockCount, int percent, int openSockets) {
        if (percent <= 0 || unlockCount <= 0 || openSockets <= 0) {
            return 0;
        }
        return Math.Max(1, unlockCount * percent / 100) * openSockets;
    }

    // The onyx crystals a piece of gear without a break table row gives, as KMS2 lists them (gear of level
    // 20 to 50, rarity 1 to 4): the item's sell price at rarity 3, divided by 600, 280, 50 or 37.5 for rarity
    // 1 to 4, rounded down, at least 1. A one-handed weapon (slot name OH) gives half. KMS2 lists the same
    // number as min and max. Nothing is known above rarity 4: those use the rarity 4 divisor.
    public static int Onyx(long sellPriceRarity3, int rarity, bool oneHanded) {
        (long multiplier, long divisor) = rarity switch {
            <= 1 => (1L, 600L),
            2 => (1L, 280L),
            3 => (1L, 50L),
            _ => (2L, 75L),
        };
        if (oneHanded) {
            divisor *= 2;
        }
        long onyx = Math.Max(0, sellPriceRarity3) * multiplier / divisor;
        return (int) Math.Clamp(onyx, 1, int.MaxValue);
    }

    // The chaos onyx crystals of the same gear: 3 at rarity 4, 1 to 2 for a one-handed weapon, none below
    // rarity 4. KMS2 lists the row for every such item, with 0 to 0 too. Above rarity 4: as rarity 4.
    public static (int Min, int Max) ChaosOnyx(int rarity, bool oneHanded) {
        if (rarity < 4) {
            return (0, 0);
        }
        return oneHanded ? (1, 2) : (3, 3);
    }

    // The amount paid for a reward listed as min to max: both ends can come out (KMS2 pays the max too).
    public static int Roll(int min, int max, Random random) {
        return max <= min ? min : random.Next(min, max + 1);
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
