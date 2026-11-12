namespace Maple2.Server.Core.Helpers;

// How the World server gives out channel ids, and how a running Game server gets its own id back from a World
// server that was restarted.
public static class ChannelRegisterRules {
    // A Game server asks again this often whether the World server still knows its channel.
    public const int RegisterIntervalMs = 5000;

    // The id for a channel that is not registered yet, -1 when there is none. A Game server that names its own id
    // (it ran before this World server started) gets that id; the instanced-content channel is always 0 and the
    // others are 1 and up. Without a named id: 0 for instanced content, else the smallest free id from 1.
    public static int Pick(ICollection<int> used, bool instancedContent, int? requested) {
        if (requested is int id) {
            if (used.Contains(id) || instancedContent != (id == 0) || id < 0) {
                return -1;
            }
            return id;
        }

        if (instancedContent) {
            return used.Contains(0) ? -1 : 0;
        }

        int channel = 1;
        while (used.Contains(channel)) {
            channel++;
        }
        return channel;
    }

    // A channel whose Game server has not answered a health check yet is still starting (it loads its data
    // before it listens): it keeps its id this long. Chosen here: two minutes.
    public const long StartGraceMs = 120_000;

    // Whether a health check that got no answer takes the channel out (marks it inactive, which frees its id for
    // the next Game server on the same address). A channel that served is out at once; a starting one only after
    // the start grace, else a second Game server started at the same time would be given the first one's id and
    // ports.
    public static bool UnansweredCheckEndsChannel(bool served, long pendingMs) {
        return served || pendingMs >= StartGraceMs;
    }

    // Whether a registered channel belongs to the Game server that names it: the same process (its instance
    // name), not only the same address. A second Game server on the same address does not take the id over.
    public static bool SameServer(string registeredInstance, string askingInstance) {
        return registeredInstance.Length > 0 && registeredInstance == askingInstance;
    }

    // Whether a failed call means the World server is not there (or too slow), as opposed to a call it answered
    // with a refusal.
    public static bool WorldGone(Grpc.Core.StatusCode status) {
        return status is Grpc.Core.StatusCode.Unavailable or Grpc.Core.StatusCode.DeadlineExceeded
            or Grpc.Core.StatusCode.Cancelled;
    }

    // Whether a player's party on the Game server is gone: the World server no longer has the player in it.
    // Parties live in the World server's memory only, so none survives its restart.
    public static bool PartyGone(int heldPartyId, int worldPartyId) {
        return heldPartyId != 0 && heldPartyId != worldPartyId;
    }
}
