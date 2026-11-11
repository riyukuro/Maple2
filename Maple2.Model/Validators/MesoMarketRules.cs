namespace Maple2.Model.Validators;

// MesoMarket 0x0081.
public static class MesoMarketRules {
    // Mode 7: i64 lowest, i64 highest meso amount. The client's search button refuses a lowest
    // amount of 0 and a lowest above the highest.
    public static bool IsSearchRange(long min, long max) {
        return min > 0 && min <= max;
    }

    // Mode 5: the listings an account has up at once (s_mesoMarket_error_maxCountOwnProduct);
    // the count per day is a second, separate limit.
    public static bool HasListingSlot(int active, int limit) {
        return active < limit;
    }
}
