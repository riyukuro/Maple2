using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;

namespace Maple2.Model.Game;

// The intimacy a quest's weddingExp adds to the marriage's exp log.
public static class MarriageExpRules {
    // The log names whose mission it was: s_wedding_dialog_exp_11 (the groom's) and _12 (the bride's) are two lines, so
    // each partner who completes a mission adds their own entry, and both completing the same quest add it twice.
    // The groom is the first stored partner, as for a filed divorce (DivorceRules.FiledStatus).
    public static MarriageExpType WeddingMission(long characterId, long firstId) {
        return characterId == firstId ? MarriageExpType.WeddingMissionGroom : MarriageExpType.WeddingMissionBride;
    }

    // A Blessing Charm's line (function WeddingExpItem): s_wedding_dialog_exp_9 the groom's, _10 the bride's.
    public static MarriageExpType WeddingExpItem(long characterId, long firstId) {
        return characterId == firstId ? MarriageExpType.weddingExpItem : MarriageExpType.WeddingExpItemBride;
    }

    // The couple message's line (reward row coupleMessage): s_wedding_dialog_exp_2 the groom's, _3 the bride's.
    public static MarriageExpType CoupleMessage(long characterId, long firstId) {
        return characterId == firstId ? MarriageExpType.coupleMessage : MarriageExpType.CoupleMessageBride;
    }

    // A partner's game entry (reward row attendance): s_wedding_dialog_exp_4 the groom's, _5 the bride's.
    public static MarriageExpType Attendance(long characterId, long firstId) {
        return characterId == firstId ? MarriageExpType.AttendanceGroom : MarriageExpType.AttendanceBride;
    }

    // Whether a line may take its reward row's exp again, by the client's own rule (WeddingTable::GetRewardExp,
    // weddingrewarditem.cpp:712, with the line's last time): day passes once the calendar day changed since its last
    // entry (IsNotRecentDayChange, timeutil.cpp:344; an entry not in the past keeps it closed), month once the
    // calendar month changed (IsMonthChanged), a row without a limit always. A line with no entry is due.
    public static bool Due(IEnumerable<(MarriageExpType Type, DateTime Time)> history, MarriageExpType type, MarriageExpLimit limit, DateTime now) {
        DateTime? last = null;
        foreach ((MarriageExpType entryType, DateTime time) in history) {
            if (entryType == type && (last == null || time > last)) {
                last = time;
            }
        }
        if (last is not DateTime previous) {
            return true;
        }
        return limit switch {
            MarriageExpLimit.day => previous < now && previous.Date != now.Date,
            MarriageExpLimit.month => previous.Year * 100 + previous.Month != now.Year * 100 + now.Month,
            _ => true,
        };
    }

    // The marriage's exp as the client counts it (WeddingProxy::GetExp, weddingproxy.cpp:705) and grades it (the
    // skill page, WeddingManager::ApplyHallTicket, weddingmanager.cpp:434): the marriedLife row's exp for each calendar
    // day since the vow (GetDayNumberDifference) plus the exp the server sends, which is the log's sum; nothing below
    // 0, and 0 unless married (also while a divorce is filed). The server sends only the log's sum, as the client adds
    // the days itself.
    public static long GradeExp(MaritalStatus status, DateTime marriageTime, DateTime now, long marriedLifeExp, long logSum) {
        if (!CountsWeddingExp(status)) {
            return 0;
        }
        long total = marriedLifeExp * (now.Date - marriageTime.Date).Days + logSum;
        return Math.Max(total, 0);
    }

    // The charm's reward row limits it to month; its description gives the rule: once used, it can be used again
    // 30 days later (koritemdescription 20001703). Each partner's own use counts against their own line.
    public static readonly TimeSpan WeddingExpItemCooldown = TimeSpan.FromDays(30);

    public static TimeSpan? Cooldown(MarriageExpLimit limit) {
        return limit switch {
            MarriageExpLimit.day => TimeSpan.FromDays(1),
            MarriageExpLimit.week => TimeSpan.FromDays(7),
            MarriageExpLimit.month => WeddingExpItemCooldown,
            _ => null,
        };
    }

    // Ready unless an entry of the same line is younger than the cooldown.
    public static bool Ready(IEnumerable<(MarriageExpType Type, DateTime Time)> history, MarriageExpType type, TimeSpan? cooldown, DateTime now) {
        return cooldown is not TimeSpan period || !history.Any(exp => exp.Type == type && now - exp.Time < period);
    }

    // Only a marriage counts it, also while a divorce is filed (still married until it is complete). A single,
    // an engaged or a divorced player gets nothing.
    public static bool CountsWeddingExp(MaritalStatus status) {
        return status is MaritalStatus.Married or MaritalStatus.DivorceFiledByGroom or MaritalStatus.DivorceFiledByBride;
    }

    // The type as seen with the two partners in the other order (DivorceRules.Swapped): a character is sent its
    // marriage with the partner first, so for the first stored partner the groom's and the bride's lines change places.
    public static MarriageExpType Swapped(MarriageExpType type) {
        return type switch {
            MarriageExpType.WeddingMissionGroom => MarriageExpType.WeddingMissionBride,
            MarriageExpType.WeddingMissionBride => MarriageExpType.WeddingMissionGroom,
            MarriageExpType.weddingExpItem => MarriageExpType.WeddingExpItemBride,
            MarriageExpType.WeddingExpItemBride => MarriageExpType.weddingExpItem,
            MarriageExpType.coupleMessage => MarriageExpType.CoupleMessageBride,
            MarriageExpType.CoupleMessageBride => MarriageExpType.coupleMessage,
            MarriageExpType.AttendanceGroom => MarriageExpType.AttendanceBride,
            MarriageExpType.AttendanceBride => MarriageExpType.AttendanceGroom,
            _ => type,
        };
    }
}
