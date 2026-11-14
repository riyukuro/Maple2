using Maple2.Model.Enum;

namespace Maple2.Model.Validators;

public static class ItemSocketRules {
    // The inventory tabs the socket pages of the item socket dialog list items from (GetItemsInCategory 0 and 1 in
    // ItemSocketSystemPageSocketUnlock and the other pages), besides the equipped gear and outfits (layers 0 and 1).
    public static readonly InventoryType[] Inventories = [InventoryType.Gear, InventoryType.Outfit];

    // The client counts no sockets on an item with a skin type (an outfit) while the SkinSocket feature is off
    // (CItemSocketTable::IsSocketFeatureEnabled, commonfiles/itemsocketsystemutil.cpp), so the ingest keeps such an
    // item's socketDataId only when the feature is on for the locale.
    // The materials a socket unlock takes (RequestUnlock, unit_140f7d060_itemsocketsystempagesocketunlock.cpp:604):
    // none for an item with a skin type, else exactly FUN_140628350's count, 2 while the ItemSocketExpansion100
    // feature (id 253) is on (the ingested feature.xml; KR 49 and NA 40), else 1.
    public const string ExpansionFeature = "ItemSocketExpansion100";

    // The client counts no sockets on any item while ItemSocketSystem (id 46) is off (GetMaxSocketCount gives 0,
    // IsSocketFeatureEnabled), and its socket pages check it (ItemSocketPageBase FUN_140f6d670).
    public const string SystemFeature = "ItemSocketSystem";

    // The dialog has a socket transfer tab only while ItemSocketTransfer01 (id 167) is on (uiitemsocketsystemdialog.cpp:171
    // and 243).
    public const string TransferFeature = "ItemSocketTransfer01";

    public static int UnlockMaterials(int skinType, bool expansion) => skinType != 0 ? 0 : expansion ? 2 : 1;

    public static int SocketId(int socketDataId, int skinType, bool skinSocketEnabled) {
        return skinType != 0 && !skinSocketEnabled ? 0 : socketDataId;
    }

    // The drop table has no skin type, so a drop row's socketDataID on an outfit is kept only when the outfit still has
    // its own socket id after the ingest's SkinSocket gate (the feature is on); other items always take it.
    public static bool KeepsDropSocketId(int skinType, int itemSocketId) {
        return skinType == 0 || itemSocketId != 0;
    }

    // The client tells a skin socket from an equipment socket by the item's skin type (CItemData::HasSkinType,
    // skinType != 0, itemsocketsystemutil.cpp 0x140627930: kind 2 skin, 1 equipment). A gem put into or taken from a
    // socket counts for the skin or the equip condition of that kind, besides gemstone_puton / gemstone_putoff
    // (70600031 "Insert a gemstone into a socket" is equip_gemstone_puton).
    public static ConditionType GemstonePutOnCondition(int skinType) =>
        skinType != 0 ? ConditionType.skin_gemstone_puton : ConditionType.equip_gemstone_puton;

    public static ConditionType GemstonePutOffCondition(int skinType) =>
        skinType != 0 ? ConditionType.skin_gemstone_putoff : ConditionType.equip_gemstone_putoff;

    // itemgemstoneupgrade.xml equipPart of an outfit gem (KR: the 4022xxxx gems; the others are 3).
    public const int SkinGemEquipPart = 4;

    // The item kinds a gem row fits, as the client reads equipPart (CItemGemStoneData, itemgemstonedata.cpp:185-213):
    // part n is bit 1 << (n - 1), and a row with no part (or one below 1) fits 7. KR: part 3 (mask 4, accessories) and
    // part 4 (mask 8, outfits).
    // A bag gem upgrade takes one gem off its stack and adds the upgraded one: room is asked only of a stack of more
    // than one (`canAddOne`: a free gem slot, or a stack of the upgraded gem with room for one more).
    public static bool GemUpgradeHasRoom(int stackAmount, bool canAddOne) => stackAmount <= 1 || canAddOne;

    public static int GemMask(int equipPart) => equipPart < 1 ? 7 : 1 << (equipPart - 1);

    // The item's kind for a gem (FUN_140222ce0, unit_1402226b0_featureenumhelpers.cpp:200): an outfit (a skin type) 8, a
    // weapon 1 (CItemData::FUN_14053d2f0: equip subtypes 30-34, 40, 41, 50-56 and 80-83), armor 2 (13-17, 22), an
    // accessory 4 (12, 18-21), else 15. Type 8 and 23 items, also weapons there, have no sockets.
    public static int GemItemKind(int skinType, int subType) {
        if (skinType != 0) {
            return 8;
        }
        return subType switch {
            >= 30 and <= 34 or 40 or 41 or >= 50 and <= 56 or >= 80 and <= 83 => 1,
            >= 13 and <= 17 or 22 => 2,
            12 or >= 18 and <= 21 => 4,
            _ => 0xF,
        };
    }

    // The client offers for a socket only the gems whose row fits the item's kind (HasGemStoneMask,
    // itemsocketsystemutil.cpp:272; ItemSocketSystemPageGemStoneManage::FUN_140f70c70, which also skips a gem with no
    // gemstone upgrade row).
    public static bool GemFits(int equipPart, int itemKind) => (GemMask(equipPart) & itemKind) != 0;

    // The success rate in percent of upgrading a gem of this level, from the server constants GemStoneProbList
    // (SkinGemStoneProbList for an outfit gem), indexed by the level the upgrade gives (KR: 25 for level 2 down to 8
    // for level 10; indices 0 and 1 are 0, no upgrade gives them). The client reads neither list; it shows the rate the
    // stage answer carries (s_itemsocketsystem_success_prob) and plays the fail when the result says so. No list or no
    // entry for the level: 100.
    public static int GemstoneUpgradeRate(int level, int equipPart, IReadOnlyList<int>? gemRates, IReadOnlyList<int>? skinRates) {
        IReadOnlyList<int>? rates = equipPart == SkinGemEquipPart ? skinRates : gemRates;
        int index = level + 1;
        if (rates == null || index < 0 || index >= rates.Count) {
            return 100;
        }
        return Math.Clamp(rates[index], 0, 100);
    }

    // roll: 0-99.
    public static bool GemstoneUpgradeSucceeds(int rate, int roll) => roll < rate;
    // CItemData::FUN_14053e940 (itemdata.cpp), for an item of type 1: the equip subtypes (id digits 3-4) the socket
    // system takes. An outfit: earring, hat, top, bottom, gloves, shoes, cape, ring and overall (12-18, 20, 22); other
    // equipment: earring, necklace and ring (12, 19, 20).
    public static bool HasSocketSubtype(int subType, int skinType) {
        return skinType != 0 ? subType is >= 12 and <= 18 or 20 or 22 : subType is 12 or 19 or 20;
    }

    // GetSocketTransferCost (itemsocketsystemutil.cpp 0x1406284a0): client tag 0x58, which is the server's
    // SkinCrystal (ItemTagMap), 200 for each open socket of the source; nothing for an item without a skin type.
    public const ItemTag TransferCostTag = ItemTag.SkinCrystal;

    public static int TransferCost(int openCount, int skinType) {
        return skinType != 0 ? openCount * 200 : 0;
    }

    // CItemSocketTable::FUN_14062d980, CItemTransfer m_b41: an item may give or take sockets when it has no
    // socketDataId, or when its socket row opens fewer sockets than it can hold. With a socketDataId but no row it may not.
    public static bool TableAllowsTransfer(int socketDataId, byte? maxCount, byte? openCount) {
        if (socketDataId == 0) {
            return true;
        }
        if (maxCount == null || openCount == null) {
            return false;
        }
        return maxCount > Math.Min(openCount.Value, maxCount.Value);
    }
}
