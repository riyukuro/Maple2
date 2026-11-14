using System.Globalization;
using Maple2.Model.Game;
using Maple2.Model.Metadata;

namespace Maple2.Model.Validators;

// The merat payback as the client reads it (commonfiles/paybacktable.cpp, client/ms2/paybackmanagerclient.cpp): the
// merets an account spends on the payback products in a month come back, the month after, by the share of
// table/paybacktable.xml's adventure levels it gained in that month. The client shows the share and the merets
// from the status the server sends (PaybackPacket.Status), and computes them with the rules below.
public static class PaybackRules {
    // PaybackTable::Load: PaybackStartDate is yyyyMMdd, its 00:00:00; PaybackEndDate the same date's 23:59:59.
    // 0 (none) leaves the period unset, and the feature off.
    public static DateTime? Start(ConstantsTable constants) => FromDate(constants.PaybackStartDate);

    public static DateTime? End(ConstantsTable constants) =>
        constants.PaybackEndDate == default ? null : constants.PaybackEndDate.Date + new TimeSpan(23, 59, 59);

    // Feature 0x109 off at ingest leaves the table without levels.
    public static bool Enabled(PaybackTable table) => table.Levels.Count > 0;

    // PaybackTable::InPeriod: start <= now < end.
    public static bool InPeriod(PaybackTable table, ConstantsTable constants, DateTime now) =>
        Enabled(table) && Start(constants) is { } start && End(constants) is { } end && start <= now && now < end;

    // PaybackTable::InMonthPeriod, the months paid in: from the first month after the start's month to the end of the
    // month after the end's month.
    public static bool InMonthPeriod(PaybackTable table, ConstantsTable constants, DateTime now) {
        if (!Enabled(table) || Start(constants) is not { } start || End(constants) is not { } end) {
            return false;
        }
        return MonthStart(start).AddMonths(1) <= now && now < MonthStart(end).AddMonths(2);
    }

    // PaybackTable::IsResetWindow: the first minutes of a month's first day (the client's PaybackRewardMinutes, the
    // server's PaybackSettleMinutes), while the client keeps the dialog shut and refuses a payback product's buy
    // (notice 0xd4f).
    public static bool IsResetWindow(PaybackTable table, ConstantsTable constants, DateTime now) =>
        Enabled(table) && now.Day == 1 && now.Hour == 0 && now.Minute * 60 + now.Second <= constants.PaybackSettleMinutes * 60;

    public static int MonthKey(DateTime time) => time.Year * 100 + time.Month;

    // The payback month now is in, as yyyyMM. PaybackManagerClient::GetPeriodText shows the start's month and the month
    // after as one period (KR "2022-01-01 ~ 2022-02-28" in both months), so the month after the start's counts in the
    // start's month and is paid with it.
    public static int PeriodKey(ConstantsTable constants, DateTime now) {
        if (Start(constants) is { } start && MonthStart(now) == MonthStart(start).AddMonths(1)) {
            return MonthKey(start);
        }
        return MonthKey(now);
    }

    public static bool IsPaybackProduct(ConstantsTable constants, int productId) =>
        productId != 0 && constants.PaybackMarketProductSnList == productId;

    // PaybackTable::GetWeightedMerat: floor(amount * weight / total weight).
    public static long WeightedMerat(PaybackTable table, PaybackTable.Level level, long amount) {
        long total = TotalWeight(table);
        return total == 0 ? 0 : (long) Math.Floor((double) (amount * level.Percent) / total);
    }

    // PaybackTable::GetLevelMerat: the last level gets what the others' floors leave, so the shares add up to amount.
    public static long LevelMerat(PaybackTable table, PaybackTable.Level level, long amount) {
        int last = table.Levels[^1].AdventureLevel;
        if (level.AdventureLevel != last) {
            return WeightedMerat(table, level, amount);
        }
        long merat = amount;
        foreach (PaybackTable.Level other in table.Levels) {
            if (other.AdventureLevel != last) {
                merat -= WeightedMerat(table, other, amount);
            }
        }
        return merat;
    }

    // PaybackTable::GetMeratUpTo: the shares of every level reached.
    public static long MeratUpTo(PaybackTable table, int levels, long amount) {
        long merat = 0;
        foreach (PaybackTable.Level level in table.Levels) {
            if (level.AdventureLevel <= levels) {
                merat += LevelMerat(table, level, amount);
            }
        }
        return merat;
    }

    // PaybackTable::GetPercentUpTo: the reached levels' weight * 100 / total weight, summed in float.
    public static float PercentUpTo(PaybackTable table, int levels) {
        long total = TotalWeight(table);
        float percent = 0f;
        if (total == 0) {
            return percent;
        }
        foreach (PaybackTable.Level level in table.Levels) {
            if (level.AdventureLevel > levels) {
                break;
            }
            percent += (float) (level.Percent * 100) / total;
        }
        return percent;
    }

    // The payment of a month: its shares of the merets spent, at most PaybackMaxRewardMerat (0 = no cap).
    public static long Reward(PaybackTable table, ConstantsTable constants, int levels, long spent) {
        long reward = spent <= 0 ? 0 : MeratUpTo(table, levels, spent);
        return constants.PaybackMaxRewardMerat > 0 ? Math.Min(reward, constants.PaybackMaxRewardMerat) : reward;
    }

    // The record's levels: those gained since the month began, none for a level that went down.
    public static int LevelsGained(PaybackRecord record, int prestigeLevel) => Math.Max(0, prestigeLevel - record.StartLevel);

    // The account's record for now: the one held while it is this period's (PeriodKey); a new period's in the payback
    // period, its start level the account's level; null outside it (no month to count).
    public static PaybackRecord? Current(PaybackRecord? held, PaybackTable table, ConstantsTable constants, DateTime now, int prestigeLevel) {
        int key = PeriodKey(constants, now);
        if (held != null && held.Month == key) {
            return held;
        }
        return InPeriod(table, constants, now) ? new PaybackRecord(key, 0, prestigeLevel) : null;
    }

    // A record of an earlier period with merets spent is due to be paid.
    public static bool Due(PaybackRecord? held, ConstantsTable constants, DateTime now) =>
        held != null && held.Month < PeriodKey(constants, now) && held.Spent > 0;

    public static string FormatPercent(float percent) => percent.ToString("0.##", CultureInfo.InvariantCulture);

    private static long TotalWeight(PaybackTable table) {
        long total = 0;
        foreach (PaybackTable.Level level in table.Levels) {
            total += level.Percent;
        }
        return total;
    }

    private static DateTime MonthStart(DateTime time) => new(time.Year, time.Month, 1);

    private static DateTime? FromDate(int date) {
        if (date <= 0) {
            return null;
        }
        try {
            return new DateTime(date / 10000, date / 100 % 100, date % 100);
        } catch (ArgumentOutOfRangeException) {
            return null;
        }
    }
}
