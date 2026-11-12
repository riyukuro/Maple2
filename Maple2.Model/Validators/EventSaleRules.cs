using Maple2.Model.Enum;
using Maple2.Model.Metadata;

namespace Maple2.Model.Validators;

// The prices the sale events of the event list change. The client shows the lowered price from the same event, so
// the server has to charge the same number.
public static class EventSaleRules {
    public const int FullRate = 10000;

    // The price of a design under a SaleUgcDesign rate, as the design window computes it
    // (uiugcitemeditdialog.cpp:1007): no rate, the price; the full rate, 0; else price * (10000 - rate) / 10000 in
    // whole numbers. A rate above the full one is free as well (chosen here; the client does not test it).
    public static long UgcDesignPrice(long price, int rate) {
        if (rate <= 0 || price <= 0) {
            return price;
        }
        if (rate >= FullRate) {
            return 0;
        }
        return price * (FullRate - rate) / FullRate;
    }

    // The price of a cube under a SaleHousing event, as the build palette computes it (CGameRule, gamerule.cpp:1985):
    // the first row with the item's housing filter and the price's money type gives
    // (int) (price * (1 - rate / 10000) + 0.5) in single floats; no such row, the price.
    public static int HousingPrice(SaleHousing? sale, int filter, FurnishingCurrencyType currency, int price) {
        if (sale == null) {
            return price;
        }
        foreach (SaleHousingCube cube in sale.Cubes) {
            if (cube.Filter == filter && cube.MoneyType == (short) currency) {
                return (int) ((float) price * (1f - (float) cube.Rate / FullRate) + 0.5f);
            }
        }
        return price;
    }

    // The times the helper reward's boxes are given under FindDungeonHelperBonus events: once, and once more per
    // bonus of the running events (the client adds the events' values up). Chosen here: the client only shows the
    // event mark on the helper line while the sum is above 0; the KR rows have value 1 and their notes say one more
    // coin, or twice the coins.
    public static int HelperRewardTimes(IEnumerable<int> bonuses) {
        long sum = 0;
        foreach (int bonus in bonuses) {
            if (bonus > 0) {
                sum += bonus;
            }
        }
        return (int) Math.Min(1 + sum, MaxHelperRewardTimes);
    }

    // Chosen here: a data error cannot multiply the reward without bound.
    public const int MaxHelperRewardTimes = 10;
}
