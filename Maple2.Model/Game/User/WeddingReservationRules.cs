using Maple2.Model.Enum;
using Maple2.Model.Error;

namespace Maple2.Model.Game;

// The cancel (Wedding 0x10) and the change (0x14) of a reserved hall. The client tests these before it sends
// (WeddingProxy::CheckReservationCancel and CheckReservationChange, weddingproxy.cpp:574 and :589); the server
// repeats them on the stored hall.
public static class WeddingReservationRules {
    // The no-refund cancel box (Wedding 0x11) the client opens for these two reasons only
    // (WeddingManager::OnReservationChangeRequest, weddingmanager.cpp:617): the partner declined, or the partner
    // is not there to be asked.
    public const WeddingError NoRefundDeclined = WeddingError.decline_reservation_cancel;
    public const WeddingError NoRefundNoPartner = WeddingError.s_wedding_result_err_same_field;

    // Only the engaged couple (0x23), only the partner who reserved (0x3e), and not from modifyLimitHours
    // (WeddingHallModifyLimitHour) before the ceremony on (0x2a).
    public static WeddingError CheckEdit(MaritalStatus status, long reserverId, long characterId, long ceremonyTime, long now, int modifyLimitHours) {
        if (status != MaritalStatus.Engaged) {
            return WeddingError.s_wedding_result_err_only_promise_user;
        }
        if (reserverId != characterId) {
            return WeddingError.s_wedding_result_only_booking_character;
        }
        if (now >= ceremonyTime - modifyLimitHours * 3600L) {
            return WeddingError.s_wedding_result_late_wedding_reserve_modify_time;
        }
        return WeddingError.none;
    }

    // A new reservation (Wedding 0x0D), as the client tests it before it opens the bill (WeddingManager::CanVisit,
    // weddingmanager.cpp:1061, WeddingProxy::CheckBase and CheckVisit, weddingproxy.cpp:531 and :545): only the
    // engaged couple (0x23), not from promiseLimitDays (WeddingPromiseLimitDay) after the engagement on (0x29), and
    // a ceremony at least modifyLimitHours (WeddingHallModifyLimitHour) away (0x36). A time of 0 is left to the
    // hall time check.
    public static WeddingError CheckReserve(MaritalStatus status, long engagedTime, long ceremonyTime, long now, int promiseLimitDays, int modifyLimitHours) {
        if (status != MaritalStatus.Engaged) {
            return WeddingError.s_wedding_result_err_only_promise_user;
        }
        if (now >= engagedTime + promiseLimitDays * 86400L) {
            return WeddingError.s_wedding_result_late_promise_time;
        }
        if (ceremonyTime > 0 && ceremonyTime < now + modifyLimitHours * 3600L) {
            return WeddingError.s_wedding_result_early_wedding_reserve;
        }
        return WeddingError.none;
    }

    // A change to the hall, time and public flag already reserved: the client's 0x2c.
    public static bool SameReservation(WeddingHall current, WeddingHall wanted) {
        return current.PackageId == wanted.PackageId && current.PackageHallId == wanted.PackageHallId &&
               current.CeremonyTime == wanted.CeremonyTime && current.Public == wanted.Public;
    }
}
