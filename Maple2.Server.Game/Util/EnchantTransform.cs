using Maple2.Model.Metadata;

namespace Maple2.Server.Game.Util;

// The client's checks for the enchant transform window (CItemEnchantTransformTable, itemenchanttransformtable.cpp).
public static class EnchantTransform {
    // The row of an item, CItemEnchantTransformTable::FindByItem. The client keys its rows by the format
    // "{0}-{1}-{2}-{3}-(4)" of item id, equip slot, enchant level, rarity and required level (MakeKey); the last
    // part is the literal "(4)", so the required level and the row's inputLimitLevel take no part in the match.
    // A later row with the same key replaces an earlier one (SetAt), so the last matching row wins.
    public static ItemEnchantTransformTable.Entry? Find(ItemEnchantTransformTable table, int itemId, int slot, int enchantLevel, int rarity) {
        for (int i = table.Entries.Count - 1; i >= 0; i--) {
            ItemEnchantTransformTable.Entry entry = table.Entries[i];
            if (entry.Rarity == rarity
                && entry.EnchantLevel == enchantLevel
                && Array.IndexOf(entry.Slots, slot) >= 0
                && Array.IndexOf(entry.ItemIds, itemId) >= 0) {
                return entry;
            }
        }
        return null;
    }

    // CanTransform's lock test: a locked item, or one whose unlock time has not passed, is refused.
    public static bool Locked(bool isLocked, long unlockTime, long now) {
        return isLocked || unlockTime > now;
    }
}
