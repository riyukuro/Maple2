using Maple2.Model.Enum;

namespace Maple2.Model.Validators;

public static class ItemSocketRules {
    // The inventory tabs the socket pages of the item socket dialog list items from (GetItemsInCategory 0 and 1 in
    // ItemSocketSystemPageSocketUnlock and the other pages), besides the equipped gear and outfits (layers 0 and 1).
    public static readonly InventoryType[] Inventories = [InventoryType.Gear, InventoryType.Outfit];

    // The client counts no sockets on an item with a skin type (an outfit) while the SkinSocket feature is off
    // (CItemSocketTable::IsSocketFeatureEnabled, commonfiles/itemsocketsystemutil.cpp), so the ingest keeps such an
    // item's socketDataId only when the feature is on for the locale.
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
}
