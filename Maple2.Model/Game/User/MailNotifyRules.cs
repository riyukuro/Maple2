namespace Maple2.Model.Game;

public static class MailNotifyRules {
    // World tells the receiver's channel of a new mail only while the receiver is online. A receiver who is
    // offline (channel -1) is skipped without a log: the mail is already stored and the list loads it at login.
    public static bool Notify(PlayerInfo? receiver) => receiver is { Online: true };
}
