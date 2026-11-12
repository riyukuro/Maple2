using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;

namespace Maple2.Server.Game.Util;

// The NpcShopShowItem game event: a shop that a running row names shows only the items its running rows name (KR
// 13000001 keeps shop 254 to its two meso items; the coin items of the ended 2019 rows stay hidden). The client has
// no class for the type and shows the items the server loads.
public static class ShopShowItemRules {
    // The item ids the shop may show at localNow, or null when no running row names the shop (it shows all).
    public static IReadOnlySet<int>? Shown(IEnumerable<GameEventMetadata> events, int shopId, DateTime localNow) {
        HashSet<int>? shown = null;
        foreach (GameEventMetadata meta in events) {
            if (meta.Data is not NpcShopShowItem row || row.ShopId != shopId || !new GameEvent(meta).IsActive(localNow)) {
                continue;
            }

            shown ??= [];
            shown.UnionWith(row.ItemIds);
        }
        return shown;
    }

    public static IEnumerable<ShopItemMetadata> Filter(IEnumerable<ShopItemMetadata> items, IReadOnlySet<int>? shown) {
        return shown == null ? items : items.Where(item => shown.Contains(item.ItemId));
    }
}
