namespace Maple2.Model.Validators;

// BadgeEquip mode 4: the i16 the client turns into one of its s_gem_error_* lines.
public enum BadgeEquipError : short {
    InventoryFull = 0,
    InvalidSlot = 1,
    InvalidItem = 2,
    Expired = 3,
    PcBang = 4,
    Unknown = 5,
}

public static class BadgeRules {
    // Equip (mode 0): the item must be in the inventory, be a badge, and not be expired.
    public static BadgeEquipError? EquipError(bool owned, bool isBadge, bool expired) {
        if (!owned || !isBadge) {
            return BadgeEquipError.InvalidItem;
        }
        return expired ? BadgeEquipError.Expired : null;
    }

    // Unequip (mode 1): a known badge slot, and room in the badge tab for the badge that comes off.
    public static BadgeEquipError? UnequipError(bool knownSlot, bool equipped, int freeSlots) {
        if (!knownSlot) {
            return BadgeEquipError.InvalidSlot;
        }
        return equipped && freeSlots <= 0 ? BadgeEquipError.InventoryFull : null;
    }
}
