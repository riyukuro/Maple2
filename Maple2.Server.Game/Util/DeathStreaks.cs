using System.Collections.Concurrent;
using Maple2.Model.Validators;
using Serilog;

namespace Maple2.Server.Game.Util;

// Deaths in a row per character, for the hit count of the tombstone. Kept for the life of this game server
// process so it follows the character through field changes; a move to another channel starts a new row.
public static class DeathStreaks {
    private static readonly ConcurrentDictionary<long, (long DeathMs, int Streak)> Streaks = new();
    private static int resetWarned;

    public static int Entries => Streaks.Count;

    // Counts a death at nowMs and returns the length of the row it belongs to. Rows that have run out (of
    // any character) are dropped here, so the table holds only characters that died in the last reset time.
    public static int Count(long characterId, long nowMs, int resetSeconds) {
        if (resetSeconds <= 0) {
            // No row in the constants.
            if (Interlocked.Exchange(ref resetWarned, 1) == 0) {
                Log.Logger.Warning("The constants have no timeResetDead; tombstone hits use {Default} s", TombstoneRules.DEFAULT_RESET_SECONDS);
            }
            resetSeconds = TombstoneRules.DEFAULT_RESET_SECONDS;
        }
        foreach (KeyValuePair<long, (long DeathMs, int Streak)> row in Streaks) {
            if (row.Key != characterId && TombstoneRules.RowExpired(row.Value.DeathMs, nowMs, resetSeconds)) {
                Streaks.TryRemove(row); // only if no death changed it meanwhile
            }
        }
        int seconds = resetSeconds;
        return Streaks.AddOrUpdate(characterId,
            _ => (nowMs, 1),
            (_, last) => (nowMs, TombstoneRules.DeathStreak(last.Streak, last.DeathMs, nowMs, seconds))).Streak;
    }
}
