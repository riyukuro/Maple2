using Maple2.Server.Game.Packets;

namespace Maple2.Server.Game.Util;

public static class HomeBlockRules {
    // The client has no limit of its own (it only shows the server's "cannot block any more" notice).
    // The server uses the block limit of the buddy list (constant MaxBlockCount); this is the fallback.
    public const int DefaultMaxBlocks = 100;
    // The memo input of the client's dialog takes 50 characters (the same input as the buddy memo).
    public const int MaxMemoLength = 50;

    public static int MaxBlocks(int maxBlockCount) {
        return maxBlockCount > 0 ? maxBlockCount : DefaultMaxBlocks;
    }

    // Null when the block may be added.
    public static HomeBlockPacket.Error? CheckAdd(long ownerAccountId, long? targetAccountId, bool alreadyBlocked, int blockCount, int maxBlocks) {
        if (targetAccountId == null) {
            return HomeBlockPacket.Error.NoUser;
        }
        if (targetAccountId == ownerAccountId) {
            return HomeBlockPacket.Error.SelfBlock;
        }
        if (alreadyBlocked) {
            return HomeBlockPacket.Error.AlreadyBlocked;
        }
        if (blockCount >= maxBlocks) {
            return HomeBlockPacket.Error.MaxBlock;
        }
        return null;
    }

    public static string TrimMemo(string memo) {
        return memo.Length > MaxMemoLength ? memo[..MaxMemoLength] : memo;
    }
}
