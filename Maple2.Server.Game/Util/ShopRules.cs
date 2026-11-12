namespace Maple2.Server.Game.Util;

public static class ShopRules {
    // The client refuses to send a purchase of less than one item, so such a request is not a player's.
    // The total is checked in 64 bits: a product that does not fit an int would wrap to a small or negative price.
    public static bool TryTotalPrice(int unitPrice, int quantity, out int total) {
        total = 0;
        if (quantity <= 0 || unitPrice < 0) {
            return false;
        }

        long price = (long) unitPrice * quantity;
        if (price > int.MaxValue) {
            return false;
        }

        total = (int) price;
        return true;
    }

    public static bool RestockDue(long fixedRestockTime, long restockTime, long now) {
        return fixedRestockTime == 0 && restockTime < now;
    }

    // What the client's shop dialog refuses to sell (UIShopDialog::CanSellItem, client/ms2/uishopdialog.cpp:2076):
    // an item whose data forbids the shop sale, a locked item (locked, or unlocking and the unlock time not yet
    // reached) and an item with a gemstone in any socket.
    public static bool CanSell(bool shopSell, bool isLocked, long unlockTime, long now, bool hasGem) {
        return shopSell && !isLocked && now >= unlockTime && !hasGem;
    }

    // The client does not send a sale of less than one item. The shop pays the unit price for each item sold.
    public static bool TrySellTotal(long unitPrice, int quantity, int owned, out long total) {
        total = 0;
        if (quantity <= 0 || quantity > owned || unitPrice < 0) {
            return false;
        }

        total = unitPrice * quantity;
        return true;
    }
}
