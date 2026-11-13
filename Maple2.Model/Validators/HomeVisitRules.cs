namespace Maple2.Model.Validators;

// A visit-home request (RequestMoveField 2: the friend list, a home's web page, a password answer) before the home is
// looked up. The client runs the first two checks itself before it sends from the friend list (uicommon.cpp
// FUN_1418fd330: dead, s_home_visit_failed_dead; a map without homeReturnable outside a player-designed map,
// s_home_visit_failed_disablemap); the other ways in send without them, so the server repeats them with the same
// answers (EnterUgcMap 9 and 8).
public static class HomeVisitRules {
    public enum Refusal {
        None,
        Dead,
        DisabledMap,
        MyHome,
    }

    // homeReturnable: the map's property (CFieldData +0x5c); inUgcMap: the player stands in a home; inOwnHome: in the
    // home of the account the request names, the player's own. The friend list never sends the player's own account
    // (FUN_1418fd330 stops there without a message), but the item billboard, the home's web page and the portal of a
    // home send without that check, and the way home may name the player's own account from anywhere. Chosen here: only
    // a request from inside that home is refused, with s_home_visit_failed_myhome (EnterUgcMap 10; the client gives
    // no case that sends it), where s_home_returnable_forbidden_to_sameplace was sent before.
    public static Refusal Check(bool isDead, bool homeReturnable, bool inUgcMap, bool inOwnHome) {
        if (isDead) {
            return Refusal.Dead;
        }
        if (!homeReturnable && !inUgcMap) {
            return Refusal.DisabledMap;
        }
        return inOwnHome ? Refusal.MyHome : Refusal.None;
    }
}
