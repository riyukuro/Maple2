using Maple2.Model.Enum;
using Maple2.Model.Game.Event;
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
    // (int) (price * (1 - rate / 10000) + 0.5) in single floats; no such row, the price. Chosen here: the rate is
    // held to 0..10000, so a data error neither raises the price nor makes it negative (the client does not test it).
    public static int HousingPrice(SaleHousing? sale, int filter, FurnishingCurrencyType currency, int price) {
        if (sale == null) {
            return price;
        }
        foreach (SaleHousingCube cube in sale.Cubes) {
            if (cube.Filter == filter && cube.MoneyType == (short) currency) {
                return (int) ((float) price * (1f - (float) Math.Clamp(cube.Rate, 0, FullRate) / FullRate) + 0.5f);
            }
        }
        return price;
    }

    // The item class a SaleRemake row names (CGameRule, gamerule.cpp): 1 for equip types 13 to 17, 2 for 12 and
    // 18 to 21, 3 for a pet, else 0. A row of class 0 counts for every item.
    public static byte RemakeSaleType(int group, int type, bool isPet) {
        if (group == 1 && type is >= 13 and <= 17) {
            return 1;
        }
        if (group == 1 && type is 12 or >= 18 and <= 21) {
            return 2;
        }
        return (byte) (isPet ? 3 : 0);
    }

    // The rate of the running SaleRemake events for an item class: the higher of the class's rate and the rate of
    // class 0 (CGameRule::GetRemakeSaleRate). Of several events of one class the client keeps the highest rate
    // (Unk_GameRuleRateMap::Recompute, gamerule.h:85), so this does too.
    public static int RemakeSaleRate(IEnumerable<SaleRemake> sales, byte saleType) {
        int rate = 0;
        foreach (SaleRemake sale in sales) {
            if ((sale.ItemType == saleType || sale.ItemType == 0) && sale.Rate > rate) {
                rate = sale.Rate;
            }
        }
        return rate;
    }

    // The count of one ingredient of a change of attributes under a discount, as the dialog shows it
    // (GetDiscountedIngredientCount, itemremakescrolldata.cpp:397): no discount, the count; else the discount is
    // held to 10000 x IngredientFeeReduceLimit and the count is (count * (1 - discount / 10000) + 0.000001) in
    // doubles, cut to a whole number and at least 1. Chosen here: a count of 0 or less stays as it is.
    public static int RemakeIngredientCount(int count, int discount, float reduceLimit) {
        if (discount <= 0 || count <= 0) {
            return count;
        }
        int limit = (int) (FullRate * (double) reduceLimit + 1e-06);
        discount = Math.Min(discount, Math.Max(limit, 0));
        int result = (int) (count * (1.0 - discount / 10000.0) + 1e-06);
        return result < 1 ? 1 : result;
    }

    // The rate a shop's beauty fees are lowered by. The client keeps one rate per shop in each of two slots, picked
    // by the row's value3 (gameevent.cpp:799), and a later event of the list overwrites the slot's rate
    // (CGameRule::SetBeautyShopSaleEvent, gamerule.cpp:638); the flagged slot counts only for a PC room player
    // (PC::FUN_141658f30, pc.cpp:1823), and the server has none, so a flagged row lowers nothing. Of the running
    // unflagged events of the shop this is the rate of the last one by start, then by id: the order the event list
    // is sent in at login.
    public static int BeautyShopRate(IEnumerable<GameEvent> events, int shopId) {
        GameEvent? last = null;
        int rate = 0;
        foreach (GameEvent gameEvent in events) {
            if (gameEvent.Metadata.Data is not SaleBeautyShop sale || sale.Flag || sale.ShopId != shopId) {
                continue;
            }
            if (last == null || gameEvent.StartTime > last.StartTime
                || gameEvent.StartTime == last.StartTime && gameEvent.Id > last.Id) {
                last = gameEvent;
                rate = sale.Rate;
            }
        }
        return rate;
    }

    // A beauty fee under a rate (CGameRule::GetDiscountedPrice): price - price * rate / 10000 in unsigned 32 bit
    // whole numbers. Chosen here: the rate is held to 0..10000 (the client does not test it).
    public static int BeautyShopPrice(int price, int rate) {
        if (price <= 0 || rate <= 0) {
            return price;
        }
        uint discount = unchecked((uint) price * (uint) Math.Min(rate, FullRate)) / FullRate;
        return (int) Math.Max(0, price - discount);
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
