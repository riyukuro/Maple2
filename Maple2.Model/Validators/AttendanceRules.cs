using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;

namespace Maple2.Model.Validators;

// Attendance (c2s 0x0066). Modes 0, 1, 2 and 3 carry nothing; mode 4 is i32 event id + i64 coupon item
// uid (0 when the box's checkbox is not ticked); mode 8 is i32 event id.
public static class AttendanceRules {
    public static (int EventId, long CouponUid) ReadEarlyParticipation(IByteReader packet) {
        int eventId = packet.ReadInt();
        long couponUid = packet.ReadLong();
        return (eventId, couponUid);
    }

    public static int ReadSelectedEvent(IByteReader packet) {
        return packet.ReadInt();
    }

    // The list index of the reward for the next claim: the first claim gives the first reward.
    // -1 when every reward of the list was claimed (or the stored count is bad).
    public static int RewardIndex(int claimed, int rewardCount) {
        return claimed >= 0 && claimed < rewardCount ? claimed : -1;
    }

    // The payAttend node of an AttendGift event's value2. Null when the type is not meso or merat or
    // the numbers are bad, so the event is sent without a pay button.
    public static AttendGift.PayAttend? ParsePay(string? type, string? price, string? maxCount, string? couponId) {
        AttendGiftCurrencyType currency = type?.ToLowerInvariant() switch {
            "meso" => AttendGiftCurrencyType.Meso,
            "merat" or "meret" => AttendGiftCurrencyType.Meret,
            _ => AttendGiftCurrencyType.None,
        };
        if (currency == AttendGiftCurrencyType.None || !long.TryParse(price, out long cost) || cost < 0
            || !int.TryParse(maxCount, out int max) || max <= 0) {
            return null;
        }
        int.TryParse(couponId, out int coupon);
        return new AttendGift.PayAttend(currency, cost, max, coupon);
    }

    public static AttendGiftCurrencyType PayType(AttendGift.PayAttend? pay) {
        return pay?.Type ?? AttendGiftCurrencyType.None;
    }

    public enum PayStep { Refuse, NoCoupon, NoMeso, NoMeret, Coupon, Meso, Meret }

    // Mode 4, the client's own tests first (uiattendgiftdialog.cpp:436-469): a day is left to buy and the
    // paid-day limit is not used up. Then the coupon when its uid was sent, else the event's currency.
    // couponItemId is the id of the item behind the sent uid, 0 when the player has no such item.
    public static PayStep PayCheck(AttendGift.PayAttend? pay, int attended, int rewardCount, int paid, bool useCoupon, int couponItemId, long meso, long meret) {
        if (pay == null || pay.Type == AttendGiftCurrencyType.None || RewardIndex(attended, rewardCount) < 0
            || paid < 0 || paid >= pay.MaxCount) {
            return PayStep.Refuse;
        }
        if (useCoupon) {
            return pay.CouponItemId != 0 && couponItemId == pay.CouponItemId ? PayStep.Coupon : PayStep.NoCoupon;
        }
        if (pay.Type == AttendGiftCurrencyType.Meso) {
            return meso >= pay.Price ? PayStep.Meso : PayStep.NoMeso;
        }
        return meret >= pay.Price ? PayStep.Meret : PayStep.NoMeret;
    }
}
