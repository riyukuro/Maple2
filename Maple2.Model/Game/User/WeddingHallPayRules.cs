using Maple2.Model.Enum;

namespace Maple2.Model.Game;

public static class WeddingHallPayRules {
    // The coupon of a package's grade: packages 1 to 3 take the WeddingHallCoupon_Grade1 to 3 tags.
    public static ItemTag CouponTag(int packageId) {
        return packageId switch {
            1 => ItemTag.WeddingHallCoupon_Grade1,
            2 => ItemTag.WeddingHallCoupon_Grade2,
            3 => ItemTag.WeddingHallCoupon_Grade3,
            _ => ItemTag.None,
        };
    }

    // A coupon pays a package's hall only when it is of that package's grade and not expired, at the pay as at
    // the reservation (the pay looked only for the item, so a grade-1 coupon paid a grade-3 hall).
    public static bool CouponFits(ItemTag tag, bool expired, int packageId) {
        ItemTag wanted = CouponTag(packageId);
        return !expired && wanted != ItemTag.None && tag == wanted;
    }

    // What a hall reservation costs in merets and whether the reservation gift mail (40000008) is sent.
    // A first reservation costs the hall price, or one coupon instead of merets. A change (Wedding 0x14 sends no
    // coupon flag) costs only the price difference, which is 0 for a change of the time alone, and sends no gifts.
    // The gifts go only with a meret payment (kept from before; the data does not say what a coupon gives).
    public static (long Meret, bool Gifts) Pay(bool change, bool coupon, long hallCost, long difference) {
        if (change) {
            return (Math.Max(0, difference), false);
        }

        return coupon ? (0, false) : (hallCost, true);
    }
}
