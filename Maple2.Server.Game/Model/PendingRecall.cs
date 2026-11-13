namespace Maple2.Server.Game.Model;

/// <summary>
/// The recall scroll kinds, as the client numbers them in s2c RecallScroll and c2s RecallScroll.
/// </summary>
public enum RecallScrollType : byte {
    Party = 0,
    Guild = 1,
    Wedding = 2,
}

/// <summary>
/// A recall this player was offered: who called, to which map, and until when (server time, seconds).
/// </summary>
public record PendingRecall(long CallerId, string CallerName, int MapId, long EndTime);

/// <summary>
/// The pending recall slots of one session (GameSession.Recalls). Each call takes the array's lock.
/// </summary>
public static class RecallSlots {
    /// <summary>
    /// Stores an offered recall. True when it replaces a recall still pending of that type, whose notice must be
    /// closed first.
    /// </summary>
    public static bool Offer(PendingRecall?[] recalls, RecallScrollType type, PendingRecall recall) {
        lock (recalls) {
            bool replaced = recalls[(int) type] != null;
            recalls[(int) type] = recall;
            return replaced;
        }
    }

    /// <summary>
    /// Drops the recall pending of that type; with a callerId other than 0, only one from that caller.
    /// True when one was dropped.
    /// </summary>
    public static bool Take(PendingRecall?[] recalls, RecallScrollType type, long callerId = 0) {
        lock (recalls) {
            PendingRecall? recall = recalls[(int) type];
            if (recall == null || (callerId != 0 && recall.CallerId != callerId)) {
                return false;
            }
            recalls[(int) type] = null;
            return true;
        }
    }

    /// <summary>
    /// Drops every recall whose end time (seconds) is before now, as the answer refuses it, and returns their types.
    /// </summary>
    public static List<RecallScrollType> TakeExpired(PendingRecall?[] recalls, long now) {
        var expired = new List<RecallScrollType>();
        lock (recalls) {
            for (int i = 0; i < recalls.Length; i++) {
                if (recalls[i] is { } recall && recall.EndTime < now) {
                    recalls[i] = null;
                    expired.Add((RecallScrollType) i);
                }
            }
        }
        return expired;
    }
}
