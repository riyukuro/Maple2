using Maple2.Model.Game;
using Maple2.Model.Metadata;

namespace Maple2.Model.Validators;

// The long-term attendance calendar (GameEventLongTermAttendGift). Its dialog (client
// uilongtermattendgiftdialog.cpp) reads the player's state from account user values: 2700 the claimed days as
// "5,11,20", 2701 the attend points, 2702 the days attended. It sends EventReward 27 when the event loads (no
// body), 26 + byte to attend one missed day (0 = with a point, 1 = for merets) and 25 + int day to claim the
// reward row of that day.
public static class LongTermAttendRules {
    // The client's cap on catch-up days (GetMissedDayCount, 0x50), whatever the event's maxattendance.
    public const int ClientDayCap = 80;

    // The ItemEntity KMS2 sends for an empty item slot.
    public static readonly RewardItem EmptyItem = new(0, 1, 1);

    public enum AttendStep {
        Point,
        Merat,
        NoPoint, // no point and no missed day: the client shows err_msg_empty_point
        Full, // no missed day: the client shows err_msg_attendance_full
        NoMerat,
    }

    // "id,rank,count" (item01/item02 of a reward row). An empty or bad text is the empty slot.
    public static RewardItem ParseItem(string? text) {
        string[] parts = (text ?? string.Empty).Split(',');
        if (parts.Length != 3 || !int.TryParse(parts[0], out int itemId) || itemId <= 0
            || !short.TryParse(parts[1], out short rarity) || !int.TryParse(parts[2], out int amount) || amount <= 0) {
            return EmptyItem;
        }
        return new RewardItem(itemId, rarity, amount);
    }

    // The days the player can still attend (GetMissedDayCount): the calendar days since the event began, the
    // first day counted, at most 80, less the days attended. The server also stops at the calendar's last day.
    public static int MissedDays(DateTime begin, DateTime now, int attended, int maxAttendance) {
        int days = Math.Min(ClientDayCap, (now.Date - begin.Date).Days + 1);
        if (maxAttendance > 0) {
            days = Math.Min(days, maxAttendance);
        }
        return Math.Max(0, days - Math.Max(0, attended));
    }

    // EventReward 26: what attending one more day takes. The client checks the same before it sends, except
    // that it sends the meret attend only when the wallet has the price.
    public static AttendStep CheckAttend(bool withMerat, int points, int missed, long merets, int price) {
        if (missed <= 0) {
            return withMerat || points > 0 ? AttendStep.Full : AttendStep.NoPoint;
        }
        if (!withMerat) {
            return points > 0 ? AttendStep.Point : AttendStep.NoPoint;
        }
        return price >= 0 && merets >= price ? AttendStep.Merat : AttendStep.NoMerat;
    }

    // EventReward 25: the reward row of the day, when the day is attended and not claimed yet (the client's
    // GetDayState 1). Null otherwise.
    public static LongTermAttendGift.Reward? CheckClaim(LongTermAttendGift gift, int day, int attended, string? claimed) {
        if (day <= 0 || day > attended || ClaimedDays(claimed).Contains(day)) {
            return null;
        }
        return gift.Rewards.FirstOrDefault(reward => reward.Day == day);
    }

    public static List<int> ClaimedDays(string? claimed) {
        var days = new List<int>();
        if (string.IsNullOrEmpty(claimed)) {
            return days;
        }
        foreach (string part in claimed.Split(',')) {
            if (int.TryParse(part, out int day)) {
                days.Add(day);
            }
        }
        return days;
    }

    public static string AddClaimedDay(string? claimed, int day) {
        List<int> days = ClaimedDays(claimed);
        if (!days.Contains(day)) {
            days.Add(day);
        }
        return string.Join(',', days);
    }

    // One attend point a day: due when no point was given yet or the last one was given on an earlier day.
    // Both times are local; the server's daily reset is at local midnight.
    public static bool PointDue(DateTime? lastGrant, DateTime now) {
        return lastGrant == null || lastGrant.Value.Date < now.Date;
    }
}
