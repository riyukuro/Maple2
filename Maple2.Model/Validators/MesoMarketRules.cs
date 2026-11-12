namespace Maple2.Model.Validators;

// MesoMarket 0x0081.
public static class MesoMarketRules {
    // Mode 7: i64 lowest, i64 highest meso amount. The client's search button refuses a lowest
    // amount of 0 and a lowest above the highest.
    public static bool IsSearchRange(long min, long max) {
        return min > 0 && min <= max;
    }

    // Mode 5: the meso amount is one of the MesoMarketProductUnit amounts (the register page's dropdown), each
    // a whole count of MesoMarketBasePrice.
    public static bool IsProductUnit(long amount, long basePrice, IEnumerable<long> units) {
        return amount > 0 && basePrice > 0 && amount % basePrice == 0 && units.Contains(amount);
    }

    // Client: CMesoMarketMan::GetMinPrice and GetMaxPrice (mesomarketproductcli.cpp:531): the average price
    // times 1.0001 -/+ the range rate, cut to a whole number, then held between the lowest and highest price.
    public static (long Low, long High) PriceRange(long averagePrice, float rangeRate, long min, long max) {
        long low = (long) (averagePrice * (1.0001 - rangeRate));
        long high = (long) (averagePrice * (1.0001 + rangeRate));
        return (Math.Clamp(low, min, max), Math.Clamp(high, min, max));
    }

    // Mode 5: the price is the price per base price times the amount's count of base prices; the client tests
    // the price per base price against the range (CMesoMarketMan::CanRegister).
    public static bool IsListingPrice(long amount, long price, long basePrice, long low, long high) {
        if (basePrice <= 0 || amount < basePrice) {
            return false;
        }
        long count = amount / basePrice;
        if (price <= 0 || price % count != 0) {
            return false;
        }
        long unitPrice = price / count;
        return unitPrice >= low && unitPrice <= high;
    }

    // The average price per base price over sold listings (amount, price), held between the lowest and highest
    // price; the fallback while nothing has been sold.
    public static long AveragePrice(IEnumerable<(long Amount, long Price)> sold, long basePrice, long fallback, long min, long max) {
        long count = 0;
        long total = 0;
        if (basePrice > 0) {
            foreach ((long amount, long price) in sold) {
                if (amount < basePrice || price <= 0) {
                    continue;
                }
                count += amount / basePrice;
                total += price;
            }
        }
        return Math.Clamp(count == 0 ? fallback : total / count, min, max);
    }

    // Mode 5: the listings an account has up at once (s_mesoMarket_error_maxCountOwnProduct);
    // the count per day is a second, separate limit.
    public static bool HasListingSlot(int active, int limit) {
        return active < limit;
    }
}
