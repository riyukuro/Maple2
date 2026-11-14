using Maple2.Model.Metadata;

namespace Maple2.Model.Validators;

// The promotionContents rows (PromotionTable): when one runs and when its counter's period ends.
public static class PromotionRules {
    // A row runs from its start to its end, both on the server's local clock (as GameEvent.IsActive compares).
    public static bool Runs(PromotionTable.Entry entry, DateTime localNow) => entry.StartTime <= localNow && localNow < entry.EndTime;

    // The last second (unix) of the counter's period that holds now: the day ends at the daily reset (local midnight),
    // the week at the weekly reset (WeeklyResetRules), eventPeriod at the row's end. A counter is kept to the end of
    // the second, so a take at exactly the next reset counts in the new period.
    public static long Expiration(PromotionTable.Entry entry, DateTime localNow) => entry.LimitType switch {
        PromotionLimit.day => Seconds(localNow.Date.AddDays(1)) - 1,
        PromotionLimit.week => Seconds(WeeklyResetRules.NextStart(localNow)) - 1,
        PromotionLimit.eventPeriod => Seconds(entry.EndTime) - 1,
        _ => long.MaxValue,
    };

    // Unix seconds of a local time (a time without a zone is read as local, as DateTimeExtensions.ToEpochSeconds).
    private static long Seconds(DateTime local) => new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Local)).ToUnixTimeSeconds();
}
