using Maple2.Model.Metadata;

namespace Maple2.Model.Validators;

// Set-item bonuses, as the client's own calculator does them (CSetItemManager, setiteminfo_2.cpp; fed by
// CEquipment::FUN_1411e76c0, equipment.cpp:2923). The client adds the set's special abilities to its own
// totals (CEquipment::GetAbilityTotal, equipment.cpp:741) and shows the parts in the tooltip
// (astooltip.cpp:920); the stats themselves and the effects come from the server.
public static class SetItemRules {
    // How many pieces of each set are worn in one layer. Every worn item counts once for each set it belongs
    // to (CSetItemManager::AddItemCount, setiteminfo_2.cpp:60, counts per item, so two worn items of one id
    // count twice). Layers are counted apart: the client keys its counts by set and layer
    // (GetSetItemLayer, equip tab 0 -> 1, outfit tab 1 -> 2), so gear and outfit pieces never add up.
    public static Dictionary<int, int> Counts(IEnumerable<int[]> wornSetIds) {
        var counts = new Dictionary<int, int>();
        foreach (int[] setIds in wornSetIds) {
            foreach (int setId in setIds) {
                counts[setId] = counts.GetValueOrDefault(setId) + 1;
            }
        }
        return counts;
    }

    // Whether a worn item counts as a set piece. The client counts it only when the character can use it
    // (PC::CanUseItem, pc.cpp:4683) at its level (CItem::CanUseAtLevel, item.cpp:653; equipment.cpp:3000-3004):
    // not expired (CItem::FUN_140528980, item.cpp:1717: an expiry time that has passed, pets excepted),
    // a premium-club item only for a premium-club account (pc.cpp:4691), not bound to another character
    // (FUN_1406343b0, itemsocketsystemutil.cpp:2534), and the item's level limit not above the character's
    // level (CItemData::IsLevelAllowed, itemdata.cpp:618).
    public static bool Counted(bool expired, bool requireVip, bool vip, long boundTo, long characterId, int levelLimit, int level) {
        if (expired) {
            return false;
        }
        if (requireVip && !vip) {
            return false;
        }
        if (boundTo != 0 && boundTo != characterId) {
            return false;
        }
        return levelLimit <= level;
    }

    // Whether a worn item's own stats join the character's totals: only an item that counts (Counted), in
    // the equip tab (layer 0) or the outfit tab (layer 1). The client adds a worn item's stats in the same
    // branch that counts it as a set piece (CEquipment::FUN_1411e76c0, equipment.cpp:2999-3020); the second
    // outfit layer (2) has its own branch with no stats (equipment.cpp:2977-2998).
    public static bool GivesStats(int layer, bool counted) {
        return counted && layer is 0 or 1;
    }

    // An item's expiry time has passed (CItem::IsExpired, item.cpp:1303; 0 = no expiry). A pet item is never
    // held back by it (item.cpp:1719).
    public static bool Expired(long expiryTime, long now, bool pet) {
        return !pet && expiryTime > 0 && expiryTime <= now;
    }

    // The item id a set lists a piece under: the item's origin id when it has one, else its own id
    // (CItemData::GetOriginOrItemId, itemdata.cpp:477; used by CSetItemManager::AddItemCount,
    // setiteminfo_2.cpp:70-73, and the tooltip, astooltip.cpp:924).
    public static int SetKey(int itemId, int originId) {
        return originId != 0 ? originId : itemId;
    }

    // The parts of a set that hold with this many pieces worn: every part asking for 1 to count pieces, not
    // only the highest one (SetItemInfo::GetOptionParts, setiteminfo.cpp:148).
    public static IEnumerable<SetBonusMetadata> Active(SetBonusMetadata[] options, int count) {
        return options.Where(option => option.Count >= 1 && option.Count <= count);
    }

    // All parts that hold for the worn layers (each layer is the set ids of its worn items).
    public static List<SetBonusMetadata> Bonuses(IEnumerable<IEnumerable<int[]>> layers, IReadOnlyDictionary<int, SetItemTable.Entry> sets) {
        var bonuses = new List<SetBonusMetadata>();
        foreach (IEnumerable<int[]> layer in layers) {
            foreach ((int setId, int count) in Counts(layer)) {
                if (sets.TryGetValue(setId, out SetItemTable.Entry? entry)) {
                    bonuses.AddRange(Active(entry.Options, count));
                }
            }
        }
        return bonuses;
    }

    // The effects of the parts that hold, once each; a part's empty effect entry (id 0) is skipped as in
    // CSetItemManager::Refresh (setiteminfo_2.cpp:284).
    public static List<(int Id, short Level)> Effects(IEnumerable<SetBonusMetadata> bonuses) {
        var effects = new List<(int Id, short Level)>();
        foreach (SetBonusMetadata bonus in bonuses) {
            foreach (SetBonusAdditionalEffect effect in bonus.AdditionalEffects) {
                if (effect.Id > 0 && !effects.Contains((effect.Id, effect.Level))) {
                    effects.Add((effect.Id, effect.Level));
                }
            }
        }
        return effects;
    }
}
