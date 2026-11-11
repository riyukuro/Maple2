namespace Maple2.Model.Validators;

public static class GuildRules {
    // Rank 0 is the master's rank.
    public const byte MasterRank = 0;

    // Guild 10 (set a member's rank): the client sends a rank from 1 to the last rank index and never 0.
    // Rank 0 moves only with the master change (Guild 63).
    public static bool IsAssignableRank(int rank, int rankCount) {
        return rank > MasterRank && rank < rankCount;
    }

    // Guild 64 (set a guild text) carries a kind byte before the text. Kind 1 is the guild notice, written by a
    // member with the notice permission, at most 300 characters. Kind 0 is a second text of at most 1000
    // characters that only the master may set; the 2525 client has no caller for it and the server keeps no such
    // text, so only kind 1 is stored.
    public const byte NoticeKind = 1;
    public const int MaxNoticeLength = 300;

    public static bool IsNotice(byte kind, string text) {
        return kind == NoticeKind && text.Length <= MaxNoticeLength;
    }

    // The master keeps rank 0 until the master changes.
    public static bool CanChangeRankOf(byte memberRank) {
        return memberRank != MasterRank;
    }
}
