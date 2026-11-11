namespace Maple2.Server.Game.Util;

// The period of the second outfit tab (the client's codi tab, CEquipmentSlotLayerInfo), in unix seconds.
// The states and the extend rule are the client's (GetPeriodState and the ItemActiveCodiTab item function).
public static class SkinTabPeriod {
    public const int SecondsPerDay = 86400;

    public enum State {
        NotStarted, // start 0
        Active, // start set, end in the future
        Limitless, // start set, end 0
        Expired, // start set, end passed
    }

    public static State GetState(long start, long end, long now) {
        if (start == 0) {
            return State.NotStarted;
        }
        if (end == 0) {
            return State.Limitless;
        }
        return now >= end ? State.Expired : State.Active;
    }

    public static bool IsActive(long start, long end, long now) {
        return GetState(start, end, now) is State.Active or State.Limitless;
    }

    // days 0 = no end. False when the client refuses the item: the tab already has no end, or the extended
    // period would span more than maxDays from its start.
    public static bool TryActivate(long start, long end, long now, int days, int maxDays, out long newStart, out long newEnd) {
        newStart = start;
        newEnd = end;
        if (days < 0) {
            return false;
        }

        switch (GetState(start, end, now)) {
            case State.Limitless:
                return false;
            case State.Active:
                if (days == 0) {
                    newEnd = 0;
                    return true;
                }

                long extended = end + (long) days * SecondsPerDay;
                long span = extended - start;
                long spanDays = span / SecondsPerDay;
                // The client compares whole days, then the seconds field of the span (0-59), not the remainder.
                if (spanDays > maxDays || (spanDays == maxDays && span % 60 > 0)) {
                    return false;
                }
                newEnd = extended;
                return true;
            default:
                newStart = now;
                newEnd = days == 0 ? 0 : now + (long) days * SecondsPerDay;
                return true;
        }
    }
}
