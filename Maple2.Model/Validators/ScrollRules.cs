using System;

namespace Maple2.Model.Validators;

// ItemRepack 0x008C mode 1: i64 scroll uid, i64 target uid.
// ItemSocketScroll 0x008B mode 1: i64 target uid, i64 scroll uid.
// ItemExtractionScroll 0x00A0: i64 scroll uid, i64 source uid.
public static class ScrollRules {
    // CItemRePackingScrollTable::Check: the item level has to lie inside the scroll's range.
    public static bool RepackLevelAllowed(int level, int min, int max) {
        return level >= min && level <= max;
    }

    // The socket scroll check treats a maximum of 0 as no upper bound.
    public static bool SocketLevelAllowed(int level, int min, int max) {
        return level >= min && (max == 0 || level <= max);
    }

    // CItem::GetRemainingRePackingCount: the item's repack limit less the repacks already done.
    public static bool HasRepackLeft(int limit, int used) {
        return limit - used > 0;
    }

    // An item with no scroll list takes any repacking scroll; otherwise the scroll has to be listed.
    public static bool RepackScrollAllowed(int[] itemScrollIds, int scrollId) {
        return itemScrollIds.Length == 0 || Array.IndexOf(itemScrollIds, scrollId) >= 0;
    }

    // A pet scroll takes pets only; an item scroll takes the equip slots it lists.
    public static bool RepackSlotAllowed(bool petScroll, bool petItem, int[] slots, int slot) {
        return petScroll ? petItem : Array.IndexOf(slots, slot) >= 0;
    }

    // CItemRePackingScrollTable::Check: an item whose enchant history is switched off is refused
    // with the remain-count message (10).
    public static bool RepackEnchantAllowed(bool hasEnchant, bool enabled) {
        return !hasEnchant || enabled;
    }

    // CItemData: only transfer types 3 and 5 (bind on equip, bind on trade) can be repacked (8).
    public static bool RepackTransferTypeAllowed(int transferType) {
        return transferType is 3 or 5;
    }

    // The test before all the others in CItemRePackingScrollTable::Check
    // (commonfiles/itemrepackingscrolldata.cpp:151, CItem::FUN_140528980 at commonfiles/item.cpp:1717): an
    // item past its expiry that is not a pet answers 2. The client lets an expired item with a maid id
    // through and also tests a music score's plays; this server has no maid id in its item data and no
    // score can be repacked (the slot test refuses it), so neither is modeled.
    public static bool RepackUsable(long expiryTime, long now, bool pet) {
        return pet || expiryTime <= 0 || expiryTime > now;
    }

    // The first code CItemRePackingScrollTable::Check gives after that, in its order: enchant switched off (10), scroll
    // not in the item's list (1), slot (3), rank (4), level (5), transfer type (8), no transfer data (2),
    // trades left, tested without the limit flag (9), bound (1), no repack left (10), locked (11). 0: none.
    public static int RepackCode(bool enchantOk, bool scrollListed, bool slotOk, bool rankOk, bool levelOk, bool transferTypeOk,
                                 bool hasTransfer, int tradesLeft, bool bound, bool repackLeft, bool locked) {
        if (!enchantOk) return 10;
        if (!scrollListed) return 1;
        if (!slotOk) return 3;
        if (!rankOk) return 4;
        if (!levelOk) return 5;
        if (!transferTypeOk) return 8;
        if (!hasTransfer) return 2;
        if (tradesLeft > 0) return 9;
        if (bound) return 1;
        if (!repackLeft) return 10;
        if (locked) return 11;
        return 0;
    }

    // Scrolls charged for one repack; the client counts every stack of the scroll's item id.
    public static bool HasRepackScrolls(int have, int need) {
        return have >= Math.Max(1, need);
    }
}
