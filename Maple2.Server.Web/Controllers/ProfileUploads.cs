using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Maple2.Model.Enum;

namespace Maple2.Server.Web.Controllers;

// The profile picture uploads (ProfileAvatar, WeddingProfile) have no ticket of the game server (the client posts them
// first and names the answered path in Ugc 11 or 12 after), so each character takes one picture of each kind per Gap. Chosen here: 2 seconds; the client's own lock lets one
// upload run at a time (CUgcManager::LockForUpload) and picks the next picture once it is released.
public static class ProfileUploads {
    public static readonly TimeSpan Gap = TimeSpan.FromSeconds(2);
    private static readonly ConcurrentDictionary<(UgcType, long), DateTime> Last = new();

    public static bool Take(UgcType type, long characterId, DateTime now) {
        (UgcType, long) key = (type, characterId);
        while (true) {
            if (!Last.TryGetValue(key, out DateTime last)) {
                if (Last.TryAdd(key, now)) {
                    return true;
                }
                continue;
            }
            if (now - last < Gap) {
                return false;
            }
            if (Last.TryUpdate(key, now, last)) {
                return true;
            }
        }
    }

    // Gives back the turn taken at now (its file was not written), unless a later upload took one since.
    public static void GiveBack(UgcType type, long characterId, DateTime now) {
        Last.TryRemove(new KeyValuePair<(UgcType, long), DateTime>((type, characterId), now));
    }
}
