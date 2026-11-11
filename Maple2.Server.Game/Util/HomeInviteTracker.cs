namespace Maple2.Server.Game.Util;

// The home invites one player has sent that are still waiting for an answer, kept in memory on the inviter's session.
// An invited client's answer is passed on to the inviter only while its entry is live; any answer removes the entry.
// The invited client's box closes itself after 20 s (CSceneStateGame::OnHomeInviteRequest, SetTimer(0x14)) and then
// answers "timed out", so an entry lives a little longer than that.
public sealed class HomeInviteTracker {
    public const long LifetimeMs = 30_000;

    private readonly Dictionary<long, long> expiryByInvitee = new();

    // Records an invite to the character; a second invite to the same character replaces the first.
    public void Add(long inviteeCharacterId, long nowMs) {
        lock (expiryByInvitee) {
            foreach (long expired in expiryByInvitee.Where(entry => entry.Value <= nowMs).Select(entry => entry.Key).ToList()) {
                expiryByInvitee.Remove(expired);
            }
            expiryByInvitee[inviteeCharacterId] = nowMs + LifetimeMs;
        }
    }

    // Removes the character's entry; true when there was one and it had not expired.
    public bool TryAnswer(long inviteeCharacterId, long nowMs) {
        lock (expiryByInvitee) {
            return expiryByInvitee.Remove(inviteeCharacterId, out long expiry) && nowMs < expiry;
        }
    }
}
