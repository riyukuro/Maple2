using Maple2.Server.Game.Packets;

namespace Maple2.Server.Game.Util;

public static class HomeBlockRules {
    // The client has no limit of its own (it only shows the server's "cannot block any more" notice).
    public const int MaxBlocks = 100;
    // The memo field of the client's dialog; the column holds 255.
    public const int MaxMemoLength = 255;

    // Null when the block may be added.
    public static HomeBlockPacket.Error? CheckAdd(long ownerAccountId, long? targetAccountId, bool alreadyBlocked, int blockCount) {
        if (targetAccountId == null) {
            return HomeBlockPacket.Error.NoUser;
        }
        if (targetAccountId == ownerAccountId) {
            return HomeBlockPacket.Error.SelfBlock;
        }
        if (alreadyBlocked) {
            return HomeBlockPacket.Error.AlreadyBlocked;
        }
        if (blockCount >= MaxBlocks) {
            return HomeBlockPacket.Error.MaxBlock;
        }
        return null;
    }

    public static string TrimMemo(string memo) {
        return memo.Length > MaxMemoLength ? memo[..MaxMemoLength] : memo;
    }
}
