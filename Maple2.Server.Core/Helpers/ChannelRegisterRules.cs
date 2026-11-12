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

    // Whether a registered channel belongs to the Game server that names it: the same process (its instance
    // name), not only the same address. A second Game server on the same address does not take the id over.
    public static bool SameServer(string registeredInstance, string askingInstance) {
        return registeredInstance.Length > 0 && registeredInstance == askingInstance;
    }

    // Whether a player's party on the Game server is gone: the World server no longer has the player in it.
    // Parties live in the World server's memory only, so none survives its restart.
    public static bool PartyGone(int heldPartyId, int worldPartyId) {
        return heldPartyId != 0 && heldPartyId != worldPartyId;
    }
}
