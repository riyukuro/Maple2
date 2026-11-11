namespace Maple2.Model.Validators;

// Ugc 0x0038, the banner modes. Mode 19 reserves hours of a banner: i64 banner id, i32 count, then per
// hour i64, i32, i64, i32 date (yyyymmdd), i32 hour, i64. Mode 1 with the banner kind then pays for the
// reserved slots: i64 banner id, u8 count and the same records, the first i64 being the slot's id.
public static class UgcRules {
    // The hour of a day and a date that exists; the slot's start time is built from them.
    public static bool BannerTimeValid(int date, int hour) {
        if (hour is < 0 or > 23) {
            return false;
        }
        int year = date / 10000;
        int month = date % 10000 / 100;
        int day = date % 100;
        return year is >= 1 and <= 9999 && month is >= 1 and <= 12 && day >= 1 && day <= DateTime.DaysInMonth(year, month);
    }

    // The price of one hour of the banner, -1 when the table has none for it.
    public static long BannerPrice(IReadOnlyList<long> prices, int hour) {
        if (hour < 0 || hour >= prices.Count || prices[hour] < 0) {
            return -1;
        }
        return prices[hour];
    }

    // The fee of an upload over its slots, or -1 when the upload is refused whole: a slot that is not
    // reserved, one that already shows a design, one named twice, or an hour without a price. Nothing
    // is taken before every slot has passed.
    public static long BannerUploadFee(IReadOnlyList<long> prices, IReadOnlyList<(long SlotId, bool Found, bool Taken, int Hour)> slots) {
        if (slots.Count == 0) {
            return -1;
        }
        var seen = new HashSet<long>();
        long total = 0;
        foreach ((long slotId, bool found, bool taken, int hour) in slots) {
            long price = BannerPrice(prices, hour);
            if (!found || taken || price < 0 || !seen.Add(slotId)) {
                return -1;
            }
            total += price;
        }
        return total;
    }
}
