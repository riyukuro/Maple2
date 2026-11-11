using Maple2.Model.Error;

namespace Maple2.Model.Game.Club;

// Club sub-type 12: the leader hands the club to a member picked by name.
public static class ClubLeaderRule {
    // Returns none and the member's character id, or the error the client shows.
    public static ClubError Pick(long leaderId, long requesterId, IEnumerable<(long Id, string Name)> members, string name, out long newLeaderId) {
        newLeaderId = 0;
        if (requesterId != leaderId) {
            return ClubError.s_club_err_no_master;
        }
        foreach ((long id, string memberName) in members) {
            if (!string.Equals(memberName, name, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            if (id == leaderId) {
                // The client does not send its own name.
                return ClubError.s_club_err_unknown;
            }
            newLeaderId = id;
            return ClubError.none;
        }
        return ClubError.s_club_err_null_member;
    }
}
