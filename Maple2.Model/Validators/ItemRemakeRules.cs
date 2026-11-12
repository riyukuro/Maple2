using System;
using System.Collections.Generic;
using Maple2.Model.Enum;
using Maple2.PacketLib.Tools;

namespace Maple2.Model.Validators;

// ChangeAttributes 0x0075 mode 0 and ChangeAttributesScroll 0x0076 mode 1 end with the options the
// player keeps: u8 count, then per option u8 kind (0 basic, 1 special) and i16 attribute.
public static class ItemRemakeRules {
    public readonly record struct Lock(bool Special, short Attribute);

    public static List<Lock> ReadLocks(IByteReader packet) {
        byte count = packet.ReadByte();
        var locks = new List<Lock>(count);
        for (int i = 0; i < count; i++) {
            bool special = packet.ReadBool();
            short attribute = packet.ReadShort();
            locks.Add(new Lock(special, attribute));
        }
        return locks;
    }

    // One lock item keeps one option; a request that keeps more is refused whole.
    public static bool LocksAllowed(int count) => count is 0 or 1;

    // The client's lock item (GetLockItemId, GetLockItemLevel; itemremakescrolldata.cpp:407): the lock tag of the
    // item's kind (armor 102, weapon 101, accessory 103, pet 104 in its tag table), and for an equip the equip's
    // rarity, which the client asks only with OptionBlock02 (on in the played client). Any item with the tag and
    // that rarity serves; a pet's lock item may be of any rarity.
    public static ItemTag? LockTag(bool armor, bool weapon, bool accessory, bool pet) {
        if (armor) {
            return ItemTag.LockItemOptionArmor;
        }
        if (weapon) {
            return ItemTag.LockItemOptionWeapon;
        }
        if (accessory) {
            return ItemTag.LockItemOptionAccessory;
        }
        return pet ? ItemTag.LockItemOptionPet : null;
    }

    public static int LockRarity(bool pet, int rarity) => pet ? 0 : rarity;

    public static bool IsLockItem(ItemTag lockTag, int lockRarity, ItemTag tag, int rarity) =>
        tag == lockTag && (lockRarity == 0 || rarity == lockRarity);

    // The client counts only lock items the character can use (Unk_Inventory::FUN_140a5f7b0, inventory.cpp:835, with
    // PC::CanUseItem): not expired, a premium-club item only for a premium-club account, not bound to another
    // character. The same test as a worn set piece's (SetItemRules.Counted) without its level limit.
    public static bool LockItemUsable(bool expired, bool requireVip, bool vip, long boundTo, long characterId) =>
        SetItemRules.Counted(expired, requireVip, vip, boundTo, characterId, 0, 0);

    // The client keeps an attribute change of gear and one of a pet apart (conditionutil.cpp registers
    // item_remake_option 0xe1 and pet_remake_option 0xe3): "Attempt to change an item's attributes through
    // Ophelia" (70600029) counts each paid change of gear.
    public static ConditionType RemakeCondition(bool isPet) =>
        isPet ? ConditionType.pet_remake_option : ConditionType.item_remake_option;

    public const long FragmentMeso = 1000;

    // An attribute change is paid in the crystal fragments the player owns and in 1000 meso for each one
    // lacking, for a pet (PetRemakeOptionBoard::FillIngredientList, tabcontentspetremakeoption.cpp:646) and
    // for gear (UIItemRemakeOptionDialog::CanPayRemakePrice, itemremakeoptiondialog.cpp:797-800); the other
    // materials must be owned.
    public static (int Fragments, long Meso) FragmentFee(int need, long owned) {
        int fragments = (int) Math.Clamp(owned, 0, Math.Max(need, 0));
        return (fragments, (Math.Max(need, 0) - fragments) * FragmentMeso);
    }
}
