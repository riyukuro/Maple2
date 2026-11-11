namespace Maple2.Server.Game.Model;

/// <summary>
/// A recall invite (s2c RecallUser 4) this player was sent and has not answered: its id, who called, to which
/// map, and until when (server time, seconds).
/// </summary>
public record PendingUserRecall(int InviteId, long CallerId, string CallerName, int MapId, long EndTime) {
    // The client's confirm box closes itself after 20 seconds; the rest is for the way there and back.
    public const int VALID_SECONDS = 30;

    private static int lastInviteId;

    // The client keeps 0 for "no invite", so an id is never 0.
    public static int NextInviteId() {
        int id;
        do {
            id = Interlocked.Increment(ref lastInviteId);
        } while (id == 0);
        return id;
    }

    /// <summary>
    /// Whether a new invite may replace this one: none is pending, or the pending one has run out.
    /// </summary>
    public static bool CanInvite(PendingUserRecall? pending, long now) {
        return pending == null || now > pending.EndTime;
    }

    /// <summary>
    /// Whether an answer (c2s RecallUser 4) with this invite id is for the pending invite and in time.
    /// </summary>
    public static bool Matches(PendingUserRecall? pending, int inviteId, long now) {
        return pending != null && inviteId != 0 && pending.InviteId == inviteId && now <= pending.EndTime;
    }
}
