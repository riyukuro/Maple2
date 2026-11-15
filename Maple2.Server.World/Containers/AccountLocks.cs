using System.Collections.Concurrent;

namespace Maple2.Server.World.Containers;

/// <summary>
/// The account locks the game and login servers hold around a load or a save. A take returns a token and the release
/// must name it: a lock taken over after its timeout is not freed by the late release of the holder it replaced, and two
/// takes of one expired lock give it to one caller only.
/// </summary>
public sealed class AccountLocks {
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private readonly record struct Held(DateTime Taken, long Token);

    private readonly ConcurrentDictionary<long, Held> held = new();
    private readonly Func<DateTime> now;
    // Seeded from the clock, so the tokens of a World run do not repeat those of the run before.
    private long nextToken = DateTime.UtcNow.Ticks;

    public AccountLocks(Func<DateTime>? now = null) {
        this.now = now ?? (() => DateTime.UtcNow);
    }

    /// <summary>The token of the take, 0 when another holder's lock has not expired.</summary>
    public long Take(long accountId) {
        DateTime time = now();
        var mine = new Held(time, Interlocked.Increment(ref nextToken));
        while (true) {
            if (held.TryAdd(accountId, mine)) {
                return mine.Token;
            }
            if (!held.TryGetValue(accountId, out Held current)) {
                continue; // released in between
            }
            if (time - current.Taken <= Timeout) {
                return 0;
            }
            // Expired: replaced only if it is still the lock that was read, so one of two racing takes wins.
            if (held.TryUpdate(accountId, mine, current)) {
                return mine.Token;
            }
        }
    }

    /// <summary>Frees the lock when it is still the take of this token.</summary>
    public bool Release(long accountId, long token) {
        return token != 0
               && held.TryGetValue(accountId, out Held current)
               && current.Token == token
               && held.TryRemove(new KeyValuePair<long, Held>(accountId, current));
    }
}
