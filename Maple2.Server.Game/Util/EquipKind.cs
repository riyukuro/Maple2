using Maple2.Model.Enum;
using Maple2.Model.Game;

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

    // A worn item dragged onto another equip slot (ItemEquip mode 2, the client's CanSwapEquippedItems):
    // only between the two hands, only when both hold an item, and both items are of a kind worn in
    // either hand (dagger or throwing star).
    public static bool CanSwapHands(EquipSlot from, EquipSlot to, ItemType dragged, ItemType? other) {
        if (!(from is EquipSlot.LH && to is EquipSlot.RH) && !(from is EquipSlot.RH && to is EquipSlot.LH)) {
            return false;
        }

        return other != null && IsEitherHand(dragged) && IsEitherHand(other.Value);
    }

    private static bool IsEitherHand(ItemType type) {
        return type.IsDagger || type.IsThrowingStar;
    }
}
