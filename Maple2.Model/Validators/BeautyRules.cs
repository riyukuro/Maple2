using Maple2.PacketLib.Tools;

namespace Maple2.Model.Validators;

// Beauty 0x0023.
public static class BeautyRules {
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

    // Mode 22 (dye): each entry names the item by uid and by id; the two must be the same item.
    public static bool DyeItemMatches(int ownedItemId, int sentItemId) {
        return ownedItemId == sentItemId;
    }
}
