using Maple2.Model.Enum;
using Maple2.Model.Game;

namespace Maple2.Model.Validators;

// The client's CBlockChecker (commonfiles/blockchecker.cpp): one record per kind 1..10, a record is live while its
// expiry is not before the server time, and each kind blocks a fixed set of actions (IsBlockedAction).
public static class AccountBlockRules {
    public const int MinKind = 1;
    public const int MaxKind = 10;
    // CBlockChecker action ids.
    public const int ActionLogin = 1;
    public const int ActionChat = 2;
    // Kind 2 is the "Penalty" (s_admin_block_velma_*), kind 4 the UGC "Citation" (s_admin_block_velma_ugc_*).
    public const int KindPenalty = 2;
    public const int KindCitation = 4;

    // CBlockChecker::IsBlockedAction (0x1401ca7a0).
    private static readonly int[][] Actions = [
        [],
        [1],
        [2, 3, 13, 14, 4, 5, 33, 8, 9, 10, 11, 15, 16],
        [7, 8],
        [3, 13, 4, 5, 33, 8, 9, 10, 11, 15, 14, 16],
        [33, 34, 35, 37, 38, 39, 40, 20, 32],
        [2],
        [11],
        [17, 18, 19, 30, 31, 32, 20, 37, 38, 33, 34, 35],
        [1],
        [36, 34, 6, 32, 37, 39, 20],
    ];

    // The reason column's width (account-block.Reason varchar(255)).
    public const int MaxReasonLength = 255;
    // The last second a stored expiry (a DateTime) can hold: 9999-12-31 23:59:59 UTC.
    public const long MaxExpiry = 253_402_300_799;

    public static bool ExpiryFits(long expiry) => expiry is >= 0 and <= MaxExpiry;

    public static bool ValidKind(int kind) => kind is >= MinKind and <= MaxKind;

    // Kinds 1 and 9 block the login, which the server's ban table already does (Chosen here: the restrict command
    // leaves them to ban).
    public static bool GmKind(int kind) => ValidKind(kind) && !BlocksAction(kind, ActionLogin);

    public static bool BlocksAction(int kind, int action) => ValidKind(kind) && Actions[kind].Contains(action);

    // CBlockChecker::IsExpired: expiry < GetServerTime().
    public static bool IsLive(AccountBlock block, long now) => block.ExpiryTime >= now;

    // CBlockChecker::FindAction: the live record of the lowest kind that blocks the action.
    public static AccountBlock? Find(IEnumerable<AccountBlock> blocks, int action, long now) {
        return blocks.Where(block => IsLive(block, now) && BlocksAction(block.Kind, action))
            .OrderBy(block => block.Kind)
            .FirstOrDefault();
    }

    // The client's own refusal for a blocked chat line (FUN_141276f80 after FindBlockedAction(2)): a Penalty shows
    // s_admin_block_velma_notice_chat (0x433), any other kind s_admin_block_chat (0x442) with its period line.
    public static StringCode? ChatRefusal(IEnumerable<AccountBlock> blocks, long now) {
        AccountBlock? block = Find(blocks, ActionChat, now);
        if (block == null) {
            return null;
        }
        return block.Kind == KindPenalty ? StringCode.s_admin_block_velma_notice_chat : StringCode.s_admin_block_chat;
    }
}
