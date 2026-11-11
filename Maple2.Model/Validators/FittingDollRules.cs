using Maple2.Model.Enum;
using Maple2.Model.Metadata;

namespace Maple2.Model.Validators;

// The home mannequin (FittingDoll 0x42 / 0x7B). The result codes are the ones the client's doll notice reads
// (FUN_14121b840, client/ms2/fittingdollslot_1.cpp:201), each naming its s_fittingdoll_ string.
public static class FittingDollRules {
    public enum Error {
        None = 0,
        InventoryFull = 1, // s_fittingdoll_InventoryFull
        InvalidOwner = 2, // s_fittingdoll_error_invalid_owner
        ItemIsSkin = 3, // s_fittingdoll_InvalidItemType_Skin: an outfit on an equipment doll
        ItemIsEquip = 4, // s_fittingdoll_InvalidItemType_Equip: equipment on an outfit doll
        Male = 7, // s_fittingdoll_Invaliditem_male
        Female = 8, // s_fittingdoll_Invaliditem_female
        InvalidItem = 0xc, // s_fittingdoll_InvalidItem
        InvalidDoll = 0xd, // s_fittingdoll_Invalid_doll
        InvalidSlot = 0x10, // s_fittingdoll_Invalid_slot
    }

    // Whether an item fits the doll, as the client tests it before it sends a put (FittingDollPC::FUN_14121c910,
    // fittingdollslot_1.cpp:490): the item is a skin exactly when the doll is an outfit doll (type not 0), and the
    // item's gender limit allows the doll's gender.
    public static Error CanWear(FunctionCubeMetadata.FittingDollData doll, bool isSkin, Gender itemGender) {
        bool outfitDoll = doll.Type != 0;
        if (isSkin != outfitDoll) {
            return outfitDoll ? Error.ItemIsEquip : Error.ItemIsSkin;
        }
        if (itemGender != Gender.All && itemGender != doll.Gender) {
            return doll.Gender == Gender.Male ? Error.Male : Error.Female;
        }
        return Error.None;
    }

    // Whether the slot the client names is one of the item's (InvenSlot::FUN_141405a60, invenslot.cpp:675: a slot the
    // item's model has no group for is refused with 0x10). A one-handed weapon goes to either hand.
    public static bool SlotFits(EquipSlot slot, IReadOnlyCollection<EquipSlot> itemSlots) {
        if (slot is EquipSlot.SK or EquipSlot.OH or EquipSlot.ER or EquipSlot.Unknown) {
            return false;
        }
        if (itemSlots.Contains(EquipSlot.OH)) {
            return slot is EquipSlot.LH or EquipSlot.RH;
        }
        return itemSlots.Contains(slot);
    }

    // The slots an item takes on the doll: all of its slots for a two-handed weapon or an overall, else the one it is in.
    public static IEnumerable<EquipSlot> Occupies(EquipSlot slot, IReadOnlyCollection<EquipSlot> itemSlots) =>
        itemSlots.Count > 1 && !itemSlots.Contains(EquipSlot.OH) ? itemSlots.Append(slot).Distinct() : [slot];

    // The doll items an item put in slot takes off: those whose slots overlap the new item's, as equipping does.
    public static List<T> Displaced<T>(EquipSlot slot, IReadOnlyCollection<EquipSlot> itemSlots,
            IEnumerable<(EquipSlot Slot, IReadOnlyCollection<EquipSlot> ItemSlots, T Item)> worn) {
        var taken = Occupies(slot, itemSlots).ToHashSet();
        return worn.Where(entry => Occupies(entry.Slot, entry.ItemSlots).Any(taken.Contains)).Select(entry => entry.Item).ToList();
    }
}
