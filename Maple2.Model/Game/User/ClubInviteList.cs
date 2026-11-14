namespace Maple2.Model.Game;

// The club invites a player's client holds open (CClubClientManager::m_invites, clubcli.cpp:733-757). The client
// lists an invite when Club 7 comes and drops it only on Club 8, the answer to its own reply (clubcli.cpp:1125-1130).
// It counts the open invites with its clubs (IsClubLimitReached, clubcli.cpp:577-591: clubs + invites >= 3), and an
// invite that comes past that limit, or a second one of a club already listed, is dropped with no box and no answer
// (clubcli.cpp:1097-1111), so the inviter would wait for nothing. An entry is kept at most the World's 30 s invite
// time: the client's box answers on its own after 20 s.
public sealed class ClubInviteList {
    public const long Lifetime = 30_000;

    private readonly object mutex = new();
    private readonly Dictionary<long, long> expiry = new();

    // now: milliseconds of any steady clock.
    public bool Contains(long clubId, long now) {
        lock (mutex) {
            return expiry.TryGetValue(clubId, out long until) && until > now;
        }
    }

    public int Count(long now) {
        lock (mutex) {
            foreach (long clubId in expiry.Where(entry => entry.Value <= now).Select(entry => entry.Key).ToList()) {
                expiry.Remove(clubId);
            }
            return expiry.Count;
        }
    }

    public void Add(long clubId, long now) {
        lock (mutex) {
            expiry[clubId] = now + Lifetime;
        }
    }

    public void Remove(long clubId) {
        lock (mutex) {
            expiry.Remove(clubId);
        }
    }
}
