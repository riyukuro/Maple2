namespace Maple2.Model.Validators;

// Logins the world refuses before the character list (LoginResult). The client (CNetworkService::OnLoginResult,
// networkservice.cpp:991) has a box for each:
// - result 11 (AuthFailed_Maintenance): the server's own text, "\n" for a line break, under the maintenance title.
// - result 6: s_login_err_full_server, the text of the code.
// GM accounts get in through both (they open the server again after the work). A player already in game is not
// refused as a full server: the login kicks the old session (AlreadyLogin) and takes the same place.
// Full = the online characters on the listed channels reach the channels' capacity, the count the server list's
// traffic bar shows (CHANNEL_CAPACITY per channel). With no channel up the login is not refused here.
// The count is a soft cap: it is read at the login check, so logins checked at the same moment can all pass and go
// a few over. The channels themselves still take them.
public static class LoginGateRules {
    public enum Refusal {
        None,
        Maintenance,
        Full,
    }

    public static Refusal Check(bool gm, bool maintenance, bool alreadyOnline, int online, int channels, int capacity) {
        if (gm) {
            return Refusal.None;
        }
        if (maintenance) {
            return Refusal.Maintenance;
        }
        if (!alreadyOnline && channels > 0 && capacity > 0 && online >= (long) channels * capacity) {
            return Refusal.Full;
        }
        return Refusal.None;
    }
}
