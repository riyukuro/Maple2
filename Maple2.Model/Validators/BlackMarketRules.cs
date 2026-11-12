using Maple2.Model.Enum;

namespace Maple2.Model.Validators;

// BlackMarket 0x005F.
public static class BlackMarketRules {
    // Mode 2: i64 item uid, i64 price of one, i32 count. The client's count box holds 1..owned.
    public static bool IsSaleCount(int count, int owned) {
        return count > 0 && count <= owned;
    }

    // A bound of 0 or below is not set.
    public static bool IsSalePrice(long price, long min, long max) {
        if (price <= 0) {
            return false;
        }
        if (min > 0 && price < min) {
            return false;
        }
        return max <= 0 || price <= max;
    }

    // The whole sale must fit a meso amount.
    public static bool IsSaleTotal(long price, int count) {
        return price > 0 && count > 0 && price <= long.MaxValue / count;
    }

    // The register button tests the sale's total, price of one times count, against BlackMarketSellMinPrice and
    // BlackMarketSellMaxPrice (BlackMarketPageItemRegister::OnClickRegister @0x140f13e70, box 0x930), then sends
    // the price of one: 4 at 50 each (200) passes a minimum of 100.
    public static bool IsSaleListing(long price, int count, long min, long max) {
        return IsSaleTotal(price, count) && IsSalePrice(price * count, min, max);
    }

    // filledScore: a custom music score that has been written (CItem::FUN_140525790; CanListOnBlackMarket refuses it).
    public static bool CanList(bool locked, bool hasTransfer, TransferFlag flag, int remainTrades, bool bound, bool filledScore = false) {
        if (locked || filledScore) {
            return false;
        }
        return !hasTransfer || TradeRules.IsTradeable(flag, remainTrades, bound);
    }

    // Mode 5: i64 listing id, i32 count.
    public static bool IsPurchaseCount(int count, int listed) {
        return count > 0 && count <= listed;
    }

    // s_blackmarket_error_cannot_buy_ownProduct: no listing of a character on the same account.
    public static bool IsOwnListing(long listingAccountId, long accountId) {
        return listingAccountId == accountId;
    }
}
