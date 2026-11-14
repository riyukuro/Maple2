using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Maple2.Server.Web.Controllers;

// The profile picture upload has no ticket of the game server (the client posts it first and names the answered path
// in Ugc 11 after), so each character takes one picture per Gap. Chosen here: 2 seconds; the client's own lock lets one
// upload run at a time (CUgcManager::LockForUpload) and picks the next picture once it is released.
public static class ProfileUploads {
    public static readonly TimeSpan Gap = TimeSpan.FromSeconds(2);
    private static readonly ConcurrentDictionary<long, DateTime> Last = new();

    public static bool Take(long characterId, DateTime now) {
        while (true) {
            if (!Last.TryGetValue(characterId, out DateTime last)) {
                if (Last.TryAdd(characterId, now)) {
                    return true;
                }
                continue;
            }
            if (now - last < Gap) {
                return false;
            }
            if (Last.TryUpdate(characterId, now, last)) {
                return true;
            }
        }
    }

    // Gives back the turn taken at now (its file was not written), unless a later upload took one since.
    public static void GiveBack(long characterId, DateTime now) {
        Last.TryRemove(new KeyValuePair<long, DateTime>(characterId, now));
    }
}
