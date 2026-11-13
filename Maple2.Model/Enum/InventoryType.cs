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

    // The stringcommon key of the client's name for a tab (GetInventoryTabName, stringtable.cpp:931: common string ids
    // 0x259-0x265 for tabs 0-12, then the keys of tabs 13 and 14). Null for a number outside the 15 tabs, which the
    // client names with its empty string.
    public static string? StringKey(int tab) => tab switch {
        0 => "s_inventory_tab_equip",
        1 => "s_inventory_tab_skin",
        2 => "s_inventory_tab_summon",
        3 => "s_inventory_tab_material",
        4 => "s_inventory_tab_life",
        5 => "s_inventory_tab_quest",
        6 => "s_inventory_tab_gem",
        7 => "s_inventory_tab_etc",
        8 => "s_inventory_tab_mastery",
        9 => "s_inventory_tab_pet",
        10 => "s_inventory_tab_activeskill",
        11 => "s_inventory_tab_coin",
        12 => "s_inventory_tab_badge",
        13 => "s_inventory_tab_lapenshard",
        14 => "s_inventory_tab_piece",
        _ => null,
    };
}
