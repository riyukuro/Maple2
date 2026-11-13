namespace Maple2.Model.Validators;

// The design market's charge on a sold design, by the table the client shows (Ugc mode 21; the tooltip text is
// uimeratmarketcommon.cpp:999): a row's step is a sales total and its value the seller's share in percent, shown as
// the charge 100 - value for the totals step + 1 to the next row's step. KMS2 gave 210 for a design sold at 300 by a
// seller with a sales total below 5,000 (value 70).
public static class UgcMarketRules {
    // The table as KMS2 sends it at every login.
    public static readonly (long Step, int Value)[] CostRows = [
        (0, 70), (5000, 60), (10000, 50), (100000, 40), (200000, 20),
    ];

    // The seller's share for a sales total: the last row whose step is below the total, as the tooltip's ranges
    // read (1 to 5,000, then 5,001 to 10,000, ...); a total of 0 has the first row. No rows: the whole price.
    public static int SharePercent(IReadOnlyList<(long Step, int Value)> rows, long salesTotal) {
        if (rows.Count == 0) {
            return 100;
        }
        int value = rows[0].Value;
        foreach ((long step, int rowValue) in rows) {
            if (step < salesTotal) {
                value = rowValue;
            }
        }
        return Math.Clamp(value, 0, 100);
    }

    // What the seller gets for one sale, in whole numbers (rounded down): the share of the step that holds the
    // seller's sales total before this sale.
    public static long Profit(IReadOnlyList<(long Step, int Value)> rows, long salesTotal, long price) {
        if (price <= 0) {
            return 0;
        }
        return price * SharePercent(rows, salesTotal) / 100;
    }

    // The fee of putting a design on sale, as the register dialog works it out before it sends the request
    // (GetRegisterFee): the client script calcMarketRegisterFee by the seller's listings on sale that are not music
    // scores, nothing for a music score, plus the constant UGCShopAdFeeMerat when the listing is promoted.
    public static long RegisterFee(int activeCount, bool score, bool promote, int adFee, bool cn) {
        long fee = 0;
        if (!score && activeCount > 0) {
            if (cn) {
                fee = activeCount >= 9 ? 300 : activeCount >= 2 ? 200 : 120;
            } else {
                fee = activeCount >= 9 ? 100 : activeCount >= 2 ? 50 : 30;
            }
        }
        if (promote) {
            fee += Math.Max(adFee, 0);
        }
        return fee;
    }

    // The price range of the register dialog (GetSellPriceRange): the constants UGCShopSellMinPrice and
    // UGCShopSellMaxPrice; a design item's own range from the design table where that is above 0; a music score's
    // lowest price from the client script calcMarketSellPriceMinMaxMusicScore (15, and 100 under CN; its highest is
    // 0 there, which keeps the constant). Never below 0, and the highest is at least the lowest.
    public static (long Min, long Max) PriceRange(long constantMin, long constantMax, long designMin, long designMax, bool score, bool cn) {
        long min = constantMin;
        long max = constantMax;
        if (score) {
            min = cn ? 100 : 15;
        } else {
            if (designMin > 0) {
                min = designMin;
            }
            if (designMax > 0) {
                max = designMax;
            }
        }
        if (min < 0) {
            min = 0;
        }
        return (min, Math.Max(min, max));
    }

    // The dialog only sends a price inside its range (errPriceMsg); any other is refused.
    public static bool InRange((long Min, long Max) range, long price) {
        return price >= range.Min && price <= range.Max;
    }

    // A listing can be bought while it is on sale: active, its sale time not over, and a price that takes nothing
    // from the seller's side (a price below 0 would pay the buyer).
    public static bool CanBuy(bool active, long listingEndTime, long nowSeconds, long price) {
        return active && listingEndTime > nowSeconds && price >= 0;
    }
}
