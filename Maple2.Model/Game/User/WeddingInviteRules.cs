using Maple2.Model.Error;

namespace Maple2.Model.Game;

// The invitations to a reserved wedding hall (Wedding 0x1b find a character, 0x1c send). The client's own
// checks are WeddingManager::CanInvite (weddingmanager.cpp:417) and UIWeddingInvitationDialog::SendInvitation
// (uiweddinginvitationdialog.cpp:223); the server repeats them on what it stores.
public static class WeddingInviteRules {
    // The client sends no invitation from 10 minutes before the ceremony time on.
    public const int LateSeconds = 600;

    public static bool TooLate(long ceremonyTime, long now) {
        return now >= ceremonyTime - LateSeconds;
    }

    // The request as the server takes it: each character once, in the order sent, without the id 0 the client
    // gives a name that was not found.
    public static List<long> Guests(IEnumerable<long> characterIds) {
        var result = new List<long>();
        foreach (long id in characterIds) {
            if (id != 0 && !result.Contains(id)) {
                result.Add(id);
            }
        }
        return result;
    }

    // Send is false with error none for what the client has no result text for (nothing to send, too late, a
    // guest that is no character): nothing is sent back then. known is how many of the guests are characters.
    // The checks with a text are in the client's order: the count against the constant, then a guest that
    // already has an invitation, then the meso for count times the price.
    public static (bool Send, WeddingError Error, long Cost) Check(long hallId, long ceremonyTime, long now,
            IReadOnlyDictionary<long, long> invited, IReadOnlyCollection<long> guests, int known, int max, long meso, long price) {
        if (hallId == 0) {
            return (false, WeddingError.s_wedding_result_not_find_weddinghall, 0);
        }
        if (guests.Count == 0 || TooLate(ceremonyTime, now)) {
            return (false, WeddingError.none, 0);
        }
        if (invited.Count + guests.Count > max) {
            return (false, WeddingError.s_wedding_result_wedding_invitation_count, 0);
        }
        if (guests.Any(invited.ContainsKey)) {
            return (false, WeddingError.s_wedding_result_already_send_invitation, 0);
        }
        if (known != guests.Count) {
            return (false, WeddingError.none, 0);
        }

        long cost = guests.Count * Math.Max(0, price);
        if (meso < cost) {
            return (false, WeddingError.s_wedding_result_lack_meso_invitation, 0);
        }
        return (true, WeddingError.none, cost);
    }

    // A guest gets the ticket of a hall that is not the guest's own marriage's while its ceremony time is in
    // the window in which the client offers the way in (WeddingManager::CollectTickets, 1800 s either side).
    public static bool GuestTicket(IReadOnlyDictionary<long, long> invited, long characterId, long hallMarriageId, long ownMarriageId,
            long ceremonyTime, long now, int windowSeconds) {
        return hallMarriageId != ownMarriageId && invited.ContainsKey(characterId) && Math.Abs(now - ceremonyTime) <= windowSeconds;
    }
}
