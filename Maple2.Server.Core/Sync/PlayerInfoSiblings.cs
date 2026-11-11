using Maple2.Model.Enum;
using Maple2.Model.Game;

namespace Maple2.Server.Core.Sync;

public static class PlayerInfoSiblings {
    // The admin permissions belong to the account. When one character's player info takes a new value,
    // these are the account's other cached characters that still hold a different one.
    public static long[] Of(IEnumerable<PlayerInfo> cached, long characterId, AdminPermissions permissions) {
        PlayerInfo[] infos = cached.ToArray();
        PlayerInfo? self = infos.FirstOrDefault(info => info.CharacterId == characterId);
        if (self == null) {
            return [];
        }
        return infos.Where(info => info.AccountId == self.AccountId && info.CharacterId != characterId
                                   && info.AccountAdminPermissions != permissions)
            .Select(info => info.CharacterId).ToArray();
    }
}
