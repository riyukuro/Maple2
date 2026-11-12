using Maple2.Model.Enum;
using Maple2.Model.Game;
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

    // The discount of a change of attributes: the higher of the sale rate and the character's fee attribute
    // (ReduceRemakeOptionFee 169 for an item, uiitemremakeoptiondialog CanPayRemakePrice; ReducePetRemakeOptionFee
    // 173 for a pet, tabcontentspetremakeoption.cpp:625), which the client turns into a rate out of 10000 as
    // (unsigned) (total / 1000 * 10000) in single floats (itemremakescrolldata.cpp:361 and :379).
    public static int RemakeDiscount(int saleRate, long feeTotal) {
        int feeRate = feeTotal <= 0 ? 0 : (int) Math.Min((float) feeTotal / 1000f * 10000f, int.MaxValue / 2);
        return Math.Max(saleRate, feeRate);
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

    // What the running SAHotTime events add to each special attribute's total: the sum over the events
    // (CGameRule, gamerule.cpp:2896).
    public static Dictionary<SpecialAttribute, long> HotTimeBonus(IEnumerable<SAHotTime> events) {
        var sum = new Dictionary<SpecialAttribute, long>();
        foreach (SAHotTime hotTime in events) {
            foreach (SAHotTimeBonus bonus in hotTime.Bonus) {
                sum[bonus.Attribute] = sum.GetValueOrDefault(bonus.Attribute) + bonus.Value;
            }
        }
        return sum;
    }

    // The bonus of the running DungeonBonusReward events for a clear of the room: the events that name the room
    // or its group, merged as the client merges them (DungeonBonusRewardCtx::Merge, gameeventcontext.cpp:786: the
    // rolls and the two rates summed, the boxes one after the other). Null when no event is for the room. Chosen
    // here: an event that names both the room and its group counts once.
    public static DungeonBonusReward? DungeonBonus(IEnumerable<DungeonBonusReward> events, int dungeonId, DungeonGroupType groupType) {
        DungeonBonusReward? merged = null;
        foreach (DungeonBonusReward bonus in events) {
            bool room = bonus.DungeonId != 0 && bonus.DungeonId == dungeonId;
            bool group = bonus.GroupType != DungeonGroupType.none && bonus.GroupType == groupType;
            if (!room && !group) {
                continue;
            }
            merged = merged == null
                ? bonus with { DungeonId = dungeonId, GroupType = groupType }
                : merged with {
                    MesoRate = merged.MesoRate + bonus.MesoRate,
                    ExpRate = merged.ExpRate + bonus.ExpRate,
                    ItemRolls = (int) Math.Min((long) merged.ItemRolls + bonus.ItemRolls, MaxDungeonBonusRolls),
                    DropBoxIds = merged.DropBoxIds.Concat(bonus.DropBoxIds).ToArray(),
                };
        }
        return merged == null ? null : merged with { ItemRolls = Math.Clamp(merged.ItemRolls, 0, MaxDungeonBonusRolls) };
    }

    // Chosen here: the most times a clear's boxes are rolled again, so a wrong row cannot roll without end.
    public const int MaxDungeonBonusRolls = 10;

    // What a rate of the bonus adds to an amount (DungeonBonusRewardCtx::ApplyMesoRate and ApplyExpRate,
    // gameeventcontext.cpp:799): (amount * rate + 0.001) in doubles, cut to a whole number. A rate of 1 doubles.
    // Chosen here: nothing for a rate or an amount of 0 or less, and no more than int.MaxValue.
    public static int DungeonBonusAmount(long amount, float rate) {
        if (amount <= 0 || !(rate > 0f)) {
            return 0;
        }
        return (int) Math.Min((double) amount * rate + 0.001, int.MaxValue);
    }

    // The first condition item of a CollectItemGroup the player lacks, null when none: an item counts when the
    // bag holds its count of the id and rank, or, for a count of 1, when the id is worn
    // (UICollectItemGroupEventDialog::HasItem, uicollectitemgroupeventdialog.cpp:292). The client shows the event
    // with 2 to 5 conditions only, and such an event alone can be claimed.
    public static RewardItem? CollectMissing(CollectItemGroup collect, Func<int, int, int> bagCount, Func<int, bool> worn) {
        if (collect.Conditions.Length is < MinCollectConditions or > MaxCollectConditions) {
            return collect.Conditions.FirstOrDefault();
        }
        foreach (RewardItem condition in collect.Conditions) {
            if (bagCount(condition.ItemId, condition.Rarity) >= condition.Amount) {
                continue;
            }
            if (condition.Amount == 1 && worn(condition.ItemId)) {
                continue;
            }
            return condition;
        }
        return null;
    }

    public const int MinCollectConditions = 2;
    public const int MaxCollectConditions = 5;

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
