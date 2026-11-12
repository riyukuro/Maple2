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
    // The seconds played today, as the client counts them for the gift's countdown
    // (GameEventAttendGiftCli::FUN_140ce39a0, gameeventcli.cpp:530): the stored user value 106 plus the time since
    // the play start the server sent (TimeSync 3). The client counts nothing once the day after that start has
    // begun (CMainSystem::FUN_141473560, mainsystem.cpp:366), so the start never lies before today.
    public static long PlaySeconds(long stored, DateTime playStart, DateTime now) {
        DateTime from = playStart > now.Date ? playStart : now.Date;
        return Math.Max(0, stored) + Math.Max(0, (long) (now - from).TotalSeconds);
    }

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
    // A claim or a bought day is counted, and its price taken, only once the gift is with the player:
    // give returns false when there is no reward left, no item or no mail, and then nothing else runs.
    public static bool GiveThenTake(Func<bool> give, Action take) {
        if (!give()) {
            return false;
        }
        take();
        return true;
    }

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
    // The requirement kind the client reads after the play seconds (GameEventAttendGift::vfunc_4,
    // commonfiles/gameeventcontext.cpp:434), then the int value: 1 the user value Value1 is set, 2 an item
    // (Value1 is the item id the window names in s_attend_gift_item_require), 3 the user value Value1 is not
    // set, 0 none (GameEventAttendGiftCli::FUN_140ce3510, gameeventcli.cpp:437; uiattendgiftdialog.cpp:557).
    public static byte RequireKind(AttendGift.Require? require) => require?.Type switch {
        AttendGiftRequirement.UserValue => 1,
        AttendGiftRequirement.ItemId => 2,
        AttendGiftRequirement.NotUserValue => 3,
        _ => 0,
    };

    public static int RequireValue(AttendGift.Require? require) => RequireKind(require) == 0 ? 0 : require!.Value1;

    // The client's bool reading of a user value text (ParseBoolW, commonfiles/packetlogger.cpp:183).
    public static bool UserValueTrue(string? text) => !string.IsNullOrEmpty(text) && text.IndexOfAny(['1', 'o', 'O', 't', 'T']) >= 0;

    // Whether the player may attend the event. userValue is the text of the player's unexpired user value
    // of the given type with id 0, null when there is none. The client lists a kind 1 event only while the
    // value reads true and a kind 3 event only while there is no value (FUN_140ce3510). A kind 2 event needs
    // an item to start it, which the server does not do (chosen here), so it is never attended.
    public static bool RequirementMet(AttendGift.Require? require, Func<int, string?> userValue) {
        return RequireKind(require) switch {
            1 => UserValueTrue(userValue(require!.Value1)),
            2 => false,
            3 => userValue(require!.Value1) == null,
            _ => true,
        };
    }
}
