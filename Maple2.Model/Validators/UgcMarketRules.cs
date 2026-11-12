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
}
