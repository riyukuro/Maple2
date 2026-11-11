namespace Maple2.Model.Game;

public static class WeddingHallPayRules {
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
