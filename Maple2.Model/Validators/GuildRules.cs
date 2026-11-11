namespace Maple2.Model.Validators;

public static class GuildRules {
    // Rank 0 is the master's rank.
    public const byte MasterRank = 0;

    // Guild 10 (set a member's rank): the client sends a rank from 1 to the last rank index and never 0.
    // Rank 0 moves only with the master change (Guild 63).
    public static bool IsAssignableRank(int rank, int rankCount) {
        return rank > MasterRank && rank < rankCount;
    }

    // The master keeps rank 0 until the master changes.
    public static bool CanChangeRankOf(byte memberRank) {
        return memberRank != MasterRank;
    }
}
