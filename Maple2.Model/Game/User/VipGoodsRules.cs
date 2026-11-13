using Maple2.Model.Enum;

namespace Maple2.Model.Game;

// The VIP goods an account bought: how many times in the current reset window, and when it last bought them.
public class VipGoodsPurchase {
    public int GoodsId { get; set; }
    public int Count { get; set; }
    public long LastPurchase { get; set; } // epoch seconds, server local time like the other resets
}

// The purchase limit of the VIP goods shop (vipgoodstable.xml buyLimit and buyLimitResetType, CVIPGoods). The client
// (UIVIPGoodsShopDialog, PremiumClub 0xeb mode 3) keeps one Int32 per goods id and reads it as what is left: a goods
// row with a limit is hidden when the value is 0 or missing, and its "Limit {0} per account" / "Can be purchased {0}
// time(s) per day/week/month" text shows the value. The tooltips say when the count starts again: at midnight (day),
// at midnight on Friday (week; s_vip_dialog_goods_sales_tooltip_week_limit names Friday in KR and EN, so not the
// shops' Constant.ResetDay) and at midnight on the 1st (month); reset type 0 never starts again.
public static class VipGoodsRules {
    // The start of the window a purchase at `now` counts in; DateTime.MinValue when the count never resets.
    public static DateTime WindowStart(ResetType resetType, DateTime now) => resetType switch {
        ResetType.Day => now.Date,
        ResetType.Week => now.Date.AddDays(-(((int) now.DayOfWeek - (int) DayOfWeek.Friday + 7) % 7)),
        ResetType.Month => new DateTime(now.Year, now.Month, 1),
        _ => DateTime.MinValue,
    };

    // The purchases of the goods in the current window.
    public static int Bought(IEnumerable<VipGoodsPurchase> purchases, int goodsId, ResetType resetType, DateTime now) {
        VipGoodsPurchase? purchase = purchases.FirstOrDefault(entry => entry.GoodsId == goodsId);
        if (purchase == null) {
            return 0;
        }
        DateTime start = WindowStart(resetType, now);
        return start == DateTime.MinValue || purchase.LastPurchase >= new DateTimeOffset(start).ToUnixTimeSeconds() ? purchase.Count : 0;
    }

    // What the client is sent for limited goods: the purchases left (0 when none); buyLimit 0 is no limit.
    public static int Remaining(IEnumerable<VipGoodsPurchase> purchases, int goodsId, int buyLimit, ResetType resetType, DateTime now) =>
        Math.Max(0, buyLimit - Bought(purchases, goodsId, resetType, now));

    // Counts one purchase at `now`: a count of an earlier window starts again from this purchase.
    public static void Record(IList<VipGoodsPurchase> purchases, int goodsId, ResetType resetType, DateTime now) {
        int bought = Bought(purchases, goodsId, resetType, now);
        VipGoodsPurchase? purchase = purchases.FirstOrDefault(entry => entry.GoodsId == goodsId);
        if (purchase == null) {
            purchase = new VipGoodsPurchase { GoodsId = goodsId };
            purchases.Add(purchase);
        }
        purchase.Count = bought + 1;
        purchase.LastPurchase = new DateTimeOffset(now).ToUnixTimeSeconds();
    }
}
