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

    // Guild 68 (CGuildManager::SendGuildMode68): a byte, listed in the guild search or not, then the focus bits.
    // The guild window sets the 16 bits of GuildFocus.
    public const int FocusMask = 0xFFFF;

    public static bool IsFocus(byte listed, int focus) {
        return listed <= 1 && (focus & ~FocusMask) == 0;
    }

    // Guild 87 (CGuildManager::SendGuildMode87): the focus bits to look for, or all 31 bits when the player
    // ticked none. Guild 88 (SendGuildMode88): a name of 2 characters or more.
    public const int SearchAnyFocus = 0x7FFFFFFF;
    public const int MinSearchNameLength = 2;
    // The client reads any count and pages by 9. The cap is the server's own.
    public const int MaxSearchResults = 90;

    public static bool IsSearchFocus(int focus) {
        return focus == SearchAnyFocus || (focus != 0 && (focus & ~FocusMask) == 0);
    }

    public static bool IsSearchName(string name) {
        return name.Length >= MinSearchNameLength && name.Length <= 50;
    }

    // The master keeps rank 0 until the master changes.
    public static bool CanChangeRankOf(byte memberRank) {
        return memberRank != MasterRank;
    }
}
