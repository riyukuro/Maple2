using Maple2.Model.Enum;

namespace Maple2.Server.Game.Util;

// The tab an equip request names (the last byte of ItemEquip mode 0, the type byte of ItemPutOn).
public static class EquipKind {
    public const byte Gear = 0;
    public const byte Outfit = 1;
    public const byte Outfit2 = 2;

    // The second outfit tab takes items only while its period runs. Hair, face and face decoration are
    // replaced in the first tab only (they are destroyed when taken off).
    public static bool CanUseOutfit2(EquipSlot slot, bool tabActive) {
        return tabActive && slot is not (EquipSlot.HR or EquipSlot.ER or EquipSlot.FA or EquipSlot.FD);
    }
}
