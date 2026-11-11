using System.Collections.Concurrent;

namespace Maple2.Server.Core.Helpers;

public static class HeartbeatRules {
    // Whether a failed heartbeat of `askedChannel` still says something about the player: only while the
    // player's record names that channel. After a channel change the record names the new channel.
    public static bool StillOn(int askedChannel, int recordedChannel) {
        return askedChannel == recordedChannel;
    }

    // Whether a failed heartbeat marks the player offline. A player who is on the way to another channel has no
    // session on the old one; marking them offline for that moment would send "logged off" and then "logged in"
    // to buddies, party, guild, club and group chat members.
    public static bool MarksOffline(int askedChannel, int recordedChannel, bool leavingAskedChannel) {
        return StillOn(askedChannel, recordedChannel) && !leavingAskedChannel;
    }
}

// The channel changes that are under way: who left which channel, and when. An entry counts for as long as the
// migration token is valid; after that the player can no longer arrive, and a failed heartbeat counts again.
public class MigrationTracker {
    private readonly TimeSpan lifetime;
    private readonly ConcurrentDictionary<long, (int Channel, DateTime At)> leaving = new();

    public MigrationTracker(TimeSpan lifetime) {
        this.lifetime = lifetime;
    }

    // A move inside one channel (toChannel == fromChannel) is not noted: the player's session stays on that
    // channel, so a miss there means what it always means.
    public void Begin(long characterId, int fromChannel, int toChannel, DateTime now) {
        if (characterId == 0 || fromChannel == toChannel) {
            return;
        }

        leaving[characterId] = (fromChannel, now);
    }

    // The channel change is over: the record's channel was written (the login on the new channel, or a logout).
    public void End(long characterId) {
        leaving.TryRemove(characterId, out _);
    }

    public int Count => leaving.Count;

    // True while the player is on the way out of `channel`. An entry of another channel says nothing: the
    // player has arrived and the record names the new channel.
    public bool IsLeaving(long characterId, int channel, DateTime now) {
        if (!leaving.TryGetValue(characterId, out (int Channel, DateTime At) entry)) {
            return false;
        }

        if (now < entry.At || now - entry.At >= lifetime) {
            leaving.TryRemove(characterId, out _);
            return false;
        }

        return entry.Channel == channel;
    }
}
