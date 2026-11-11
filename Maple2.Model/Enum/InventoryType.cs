namespace Maple2.Model.Enum;

public enum InventoryType : byte {
    // Inventory
    Gear = 0,
    Outfit = 1,
    Mount = 2,
    Catalyst = 3,
    FishingMusic = 4,
    Quest = 5,
    Gemstone = 6,
    Misc = 7,
    // PetEquip = 8,
    LifeSkill = 9,
    Pets = 10,
    Consumable = 11,
    Currency = 12,
    Badge = 13,
    //Mushtopia = 14,
    Lapenshard = 15,
    Fragment = 16,
}

// The client numbers its inventory tabs 0-14 without gaps (Unk_Inventory's 15 categories, CItemData's tab
// from the item type): mastery 8, pet 9, active skill 10, coin 11, badge 12, lapenshard 13, piece 14.
// Every packet field that carries a tab uses the client's number.
public static class InventoryTab {
    public const int Count = 15;

    public static byte ToClient(this InventoryType type) => type switch {
        InventoryType.LifeSkill => 8,
        InventoryType.Pets => 9,
        InventoryType.Consumable => 10,
        InventoryType.Currency => 11,
        InventoryType.Badge => 12,
        InventoryType.Lapenshard => 13,
        InventoryType.Fragment => 14,
        _ => (byte) type,
    };

    // False for a number the client never sends (outside its 15 tabs).
    public static bool TryFromClient(int tab, out InventoryType type) {
        type = tab switch {
            8 => InventoryType.LifeSkill,
            9 => InventoryType.Pets,
            10 => InventoryType.Consumable,
            11 => InventoryType.Currency,
            12 => InventoryType.Badge,
            13 => InventoryType.Lapenshard,
            14 => InventoryType.Fragment,
            _ => (InventoryType) tab,
        };
        return tab is >= 0 and < Count;
    }
}
