namespace Maple2.Model.Game;

public static class WorldForwardRules {
    // World forwards a request (mail notice, buddy, marriage) to the receiver's channel only while the receiver is
    // online. A receiver who is offline (channel -1) has no channel to reach: the request is dropped without a log,
    // with the same empty reply as before, since the caller has already stored what the receiver loads at login.
    public static bool Forward(PlayerInfo? receiver) => receiver is { Online: true };
}
