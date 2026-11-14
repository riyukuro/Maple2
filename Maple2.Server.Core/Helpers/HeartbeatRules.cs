using System.Collections.Concurrent;
using Maple2.Server.Core.Network;
using Maple2.Server.Core.Packets;

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

    // The World server asks every session for a heartbeat this often.
    public const long IntervalMs = 30000;

    // A session is cut when nothing at all has arrived from its client for this long: three heartbeat rounds
    // (chosen here). The client only answers heartbeats; it keeps no time limit of its own for them, and an
    // answer that comes late says the client stood still for a while, not that it is gone.
    public const long SilenceLimitMs = 3 * IntervalMs;

    // The heartbeats that must have gone unanswered as well (chosen here). The time alone does not say that the
    // client is gone: when the rounds stop for a while (the World server restarts, its round is held up), a
    // client that only answers heartbeats had nothing to answer. Such a session is asked again first.
    public const int UnansweredLimit = 2;

    // Asks closer together than this count as one.
    public const long AskGapMs = IntervalMs / 2;

    // Whether the client has been silent for too long; both times in the same millisecond count.
    public static bool IsSilent(long lastReceivedMs, int unansweredAsks, long nowMs) {
        return unansweredAsks >= UnansweredLimit && nowMs - lastReceivedMs >= SilenceLimitMs;
    }

    // One heartbeat round for a session, on the login server and on a channel alike: a silent session is cut
    // (false), any other is asked.
    public static bool Ask(Session session, long nowMs) {
        if (IsSilent(session.LastReceivedMs, session.UnansweredAsks, nowMs)) {
            session.Cut();
            return false;
        }

        session.MarkAsked(nowMs, AskGapMs);
        session.Send(RequestPacket.Heartbeat());
        return true;
    }
}

// The channel changes that are under way: who left which channel, and when. An entry counts for as long as the
// migration token is valid; after that the player can no longer arrive, and a failed heartbeat counts again.
public class MigrationTracker {
    private readonly TimeSpan lifetime;
    private readonly ConcurrentDictionary<long, (int Channel, int To, DateTime At)> leaving = new();

    public MigrationTracker(TimeSpan lifetime) {
        this.lifetime = lifetime;
    }

    // A move inside one channel (toChannel == fromChannel) is not noted: the player's session stays on that
    // channel, so a miss there means what it always means.
    public void Begin(long characterId, int fromChannel, int toChannel, DateTime now) {
        if (characterId == 0 || fromChannel == toChannel) {
            return;
        }

        leaving[characterId] = (fromChannel, toChannel, now);
    }

    // The channel change is over: the record's channel was written (the login on the new channel, or a logout).
    public void End(long characterId) {
        leaving.TryRemove(characterId, out _);
    }

    public int Count => leaving.Count;

    // True while the player is on the way out of `channel`. An entry of another channel says nothing: the
    // player has arrived and the record names the new channel.
    public bool IsLeaving(long characterId, int channel, DateTime now) {
        return Current(characterId, now) is { } entry && entry.Channel == channel;
    }

    // The channel a player on the way out of `channel` goes to, -1 for none. The session there can be loaded
    // (and play) before its login writes the record's channel.
    public int TargetOf(long characterId, int channel, DateTime now) {
        return Current(characterId, now) is { } entry && entry.Channel == channel ? entry.To : -1;
    }

    private (int Channel, int To, DateTime At)? Current(long characterId, DateTime now) {
        if (!leaving.TryGetValue(characterId, out (int Channel, int To, DateTime At) entry)) {
            return null;
        }

        if (now < entry.At || now - entry.At >= lifetime) {
            leaving.TryRemove(characterId, out _);
            return null;
        }

        return entry;
    }
}
