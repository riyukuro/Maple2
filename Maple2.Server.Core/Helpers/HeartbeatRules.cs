namespace Maple2.Server.Core.Helpers;

public static class HeartbeatRules {
    // Whether a failed heartbeat of `askedChannel` still says something about the player: only while the
    // player's record names that channel. After a channel change the record names the new channel.
    public static bool StillOn(int askedChannel, int recordedChannel) {
        return askedChannel == recordedChannel;
    }
}
