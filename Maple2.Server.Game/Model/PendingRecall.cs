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
