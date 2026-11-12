using System.Collections.Concurrent;
using Maple2.Model.Validators;

namespace Maple2.Server.Game.Util;

// Deaths in a row per character, for the hit count of the tombstone. Kept for the life of this game server
// process so it follows the character through field changes; a move to another channel starts a new row.
public static class DeathStreaks {
    private static readonly ConcurrentDictionary<long, (long DeathMs, int Streak)> Streaks = new();

    // Counts a death at nowMs and returns the length of the row it belongs to.
    public static int Count(long characterId, long nowMs, int resetSeconds) {
        return Streaks.AddOrUpdate(characterId,
            _ => (nowMs, 1),
            (_, last) => (nowMs, TombstoneRules.DeathStreak(last.Streak, last.DeathMs, nowMs, resetSeconds))).Streak;
    }
}
