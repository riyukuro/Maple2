using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Tools.Extensions;

namespace Maple2.Model.Validators;

// Beauty 0x0023.
public static class BeautyRules {
    // A fee can be taken: the price is not negative and the character owns that much.
    public static bool Affords(long owned, long price) {
        return price >= 0 && owned >= price;
    }

    // Modes 17 and 19: the extra saved style slots after one more extension, or -1 when the extension is
    // not loaded (0 slots per extension) or would pass the most that can be bought.
    public static int ExtendedSlots(int extended, int perExtension, int maxExtra) {
        if (extended < 0 || perExtension <= 0 || extended + perExtension > maxExtra) {
            return -1;
        }
        return extended + perExtension;
    }

    public enum ExtendStep { Extend, AtMax, NoMeret }

    // One extension asked (modes 17 and 19): refused at the most with the client's code 19, refused
    // without the merets with code 20 (uibeautyshopstyledialog.cpp:1340-1351), else bought.
    public static ExtendStep ExtendCheck(int extended, int perExtension, int maxExtra, long meret, long price) {
        if (ExtendedSlots(extended, perExtension, maxExtra) < 0) {
            return ExtendStep.AtMax;
        }
        return Affords(meret, price) ? ExtendStep.Extend : ExtendStep.NoMeret;
    }

    // Mode 17 (save into a full list): the extension is bought only when no slot is open.
    public static bool SaveNeedsExtension(int openSlots) {
        return openSlots <= 0;
    }

    // The saved style slots a character has: the client's base (BeautyStyleDefaultSlotCount,
    // uibeautyshopstyledialog.cpp:426-428) plus the bought ones, never past the client's most
    // (base + BeautyStyleMaxSlotCount, :420-423). 36 stands in only for a constant that is not loaded.
    public static int StyleSlots(int baseSlots, int extended, int maxExtra) {
        if (baseSlots <= 0) {
            baseSlots = 36;
        }
        return baseSlots + Math.Clamp(extended, 0, Math.Max(maxExtra, 0));
    }

    // The slots still free for one more saved style.
    public static int OpenStyleSlots(int baseSlots, int extended, int maxExtra, int saved) {
        return Math.Max(StyleSlots(baseSlots, extended, maxExtra) - saved, 0);
    }

    // The client shows a shop item locked and keeps its save button off while the character is under the item's
    // level or the item's trophy is not past the grade (uibeautyshopstyledialog.cpp:709 FUN_140f02860, with
    // IsBeforeStartGrade, achievemanager.cpp:447; the save button, uibeautyshopdialog.cpp:1412-1421).
    public static bool IsLocked(short level, short requiredLevel, int achievementId, Func<bool> hasGrade) {
        if (requiredLevel > 0 && requiredLevel > level) {
            return true;
        }
        return achievementId > 0 && !hasGrade();
    }

    // Mode 3 (new item from the shop): u8 shop id, u8 free try, i32 item id, i64 the value the shop-open
    // packet carried, then the item's appearance data.
    public static (byte Shop, bool UseVoucher, int ItemId, long ShopValue) ReadCreate(IByteReader packet) {
        byte shop = packet.ReadByte();
        bool useVoucher = packet.ReadBool();
        int itemId = packet.ReadInt();
        long shopValue = packet.ReadLong();
        return (shop, useVoucher, itemId, shopValue);
    }

    // Mode 18 (delete a saved style): i64 item uid, u8 the slot's index in the list. The index starts at 0;
    // it is not a flag.
    public static (long Uid, byte Slot) ReadDelete(IByteReader packet) {
        long uid = packet.ReadLong();
        byte slot = packet.ReadByte();
        return (uid, slot);
    }

    // Mode 22 (dye), one entry: u8 set; when set u8, u8 free try, u8, i32, i64, i64 item uid, i32 item id,
    // then the item's colors. Null for an entry that is not set.
    public static (bool UseVoucher, long Uid, int ItemId)? ReadDyeHead(IByteReader packet) {
        if (!packet.ReadBool()) {
            return null;
        }
        packet.ReadByte();
        bool useVoucher = packet.ReadBool();
        packet.ReadByte();
        packet.ReadInt();
        packet.ReadLong();
        long uid = packet.ReadLong();
        int itemId = packet.ReadInt();
        return (useVoucher, uid, itemId);
    }

    // The colors of one dye entry: a hat carries its placement after the colors. They are read for a
    // refused entry too, so the entries after it are still read from their first byte.
    public static ItemAppearance ReadDyeColors(IByteReader packet, bool cap) {
        return cap ? packet.ReadClass<CapAppearance>() : packet.ReadClass<ItemAppearance>();
    }

    // Mode 22 (dye): each entry names the item by uid and by id; the two must be the same item.
    public static bool DyeItemMatches(int ownedItemId, int sentItemId) {
        return ownedItemId == sentItemId;
    }
}
