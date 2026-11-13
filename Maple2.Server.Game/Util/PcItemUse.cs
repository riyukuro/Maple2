using Maple2.Database.Extensions;
using Maple2.Model.Game;
using Maple2.Model.Validators;
using Maple2.Server.Game.Session;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Game.Util;

public static class PcItemUse {
    // PC::CanUseItem (pc.cpp:4683) as far as the server keeps it: not expired (pets excepted), a premium-club item
    // only with premium club, not bound to another character (ItemRemakeRules.LockItemUsable).
    // The premium club is read only for an item that asks for it.
    public static bool CanUseItem(GameSession session, Item item) {
        bool requireVip = item.Metadata.Limit.RequireVip;
        return ItemRemakeRules.LockItemUsable(SetItemRules.Expired(item.ExpiryTime, DateTime.Now.ToEpochSeconds(), item.Pet != null),
            requireVip, requireVip && session.Config.IsPremiumClubActive(), item.Transfer?.Binding?.CharacterId ?? 0, session.CharacterId);
    }
}
