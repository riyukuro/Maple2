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
