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

    // Scrolls charged for one repack; the client counts every stack of the scroll's item id.
    // CItemRePackingScrollTable::Check: an item whose enchant history is switched off is refused
    // with the remain-count message (10).
    public static bool RepackEnchantAllowed(bool hasEnchant, bool enabled) {
        return !hasEnchant || enabled;
    }

    // CItemData: only transfer types 3 and 5 (bind on equip, bind on trade) can be repacked (8).
    public static bool RepackTransferTypeAllowed(int transferType) {
        return transferType is 3 or 5;
    }

    public static bool HasRepackScrolls(int have, int need) {
        return have >= Math.Max(1, need);
    }
}
