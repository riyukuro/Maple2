using Maple2.Model.Enum;

namespace Maple2.Server.Game.Util;

public static class BuddyRules {
    // Buddy sub-type 20 carries the "refuse friend requests" game option as an int: 0 or 1
    // (CBuddyManager::SendBuddyMode20). Any other value is not a client value.
    public static bool TryAutoReject(int value, out bool on) {
        on = value == 1;
        return value is 0 or 1;
    }

    // The client declines an incoming request itself when the option is on (protocol 9 in the client's
    // buddy response handling); the server does the same for the option value the client reported.
    // The request entry is sent to the receiving client only when the server does not decline it first.
    public static bool ShowsRequest(bool autoReject, BuddyType type) {
        return !DeclinesOnReceipt(autoReject, type);
    }

    public static bool DeclinesOnReceipt(bool autoReject, BuddyType type) {
        return autoReject && type == BuddyType.InRequest;
    }
}
