using System;
using Maple2.Model.Error;

namespace Maple2.Model.Game;

// The house commendation (RequestCube 0x19, the client's CommendHome) and the month of the commendation board.
// The client keeps the time of the account's last commendation (HomeCommand 0 and ResponseCube 0x19) and enables
// the home info dialog's recommend button when IsNotRecentDayChange(that time) holds: no commendation yet, or one
// more than a day ago, or one on an earlier calendar day. The server keeps the same rule, so an account commends
// one house a day.
public static class HomeCommendRules {
    private const long DaySeconds = 86400;

    // The client's IsNotRecentDayChange (timeutil.cpp), with the server's time zone for its CMsTime days.
    public static bool MayCommend(long lastCommend, long now, TimeZoneInfo zone) {
        if (lastCommend == 0) {
            return true;
        }
        long diff = now - lastCommend;
        if (diff <= 0) {
            return false;
        }
        if (diff > DaySeconds) {
            return true;
        }
        return Local(lastCommend, zone).Date != Local(now, zone).Date;
    }

    // Why the account may not commend the home it stands in (ShowBuildNotice codes), null when it may: no home
    // owner here (76), its own home (78), a commendation earlier today (81).
    public static UgcMapError? Check(long ownerId, long accountId, long lastCommend, long now, TimeZoneInfo zone) {
        if (ownerId == 0) {
            return UgcMapError.s_ugcmap_no_owner_to_commend;
        }
        if (ownerId == accountId) {
            return UgcMapError.s_ugcmap_cant_commend_myself;
        }
        if (!MayCommend(lastCommend, now, zone)) {
            return UgcMapError.s_ugcmap_cant_commend_duplicate;
        }
        return null;
    }

    // The yyyymm month of a time, as the client builds the board's date (FUN_140b3df90).
    public static int MonthOf(long time, TimeZoneInfo zone) {
        DateTime local = Local(time, zone);
        return local.Year * 100 + local.Month;
    }

    // The first second of a yyyymm month and the first second of the month after it (FUN_140b3e050 ends one second
    // before that). Null for a date that is no month.
    public static (long Begin, long End)? MonthBounds(int yearMonth, TimeZoneInfo zone) {
        int year = yearMonth / 100;
        int month = yearMonth % 100;
        if (year < 1970 || year > 9998 || month < 1 || month > 12) {
            return null;
        }
        var begin = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        DateTime end = begin.AddMonths(1);
        return (Epoch(begin, zone), Epoch(end, zone));
    }

    // The month of a time: its first second and the first second of the month after it.
    public static (long Begin, long End) MonthSpan(long time, TimeZoneInfo zone) {
        return MonthBounds(MonthOf(time, zone), zone)!.Value;
    }

    private static DateTime Local(long time, TimeZoneInfo zone) {
        return TimeZoneInfo.ConvertTimeFromUtc(DateTimeOffset.FromUnixTimeSeconds(time).UtcDateTime, zone);
    }

    private static long Epoch(DateTime local, TimeZoneInfo zone) {
        return new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUnixTimeSeconds();
    }
}
