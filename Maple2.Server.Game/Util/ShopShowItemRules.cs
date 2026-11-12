using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;

namespace Maple2.Server.Game.Util;

// The NpcShopShowItem game event: an item that a row of its shop names (running or ended) shows only while one of
// those rows runs; the shop's items no row names always show (KR shop 254: the coin items of the ended 2019 rows
// stay hidden, the two meso items of the running 13000001 show). The client has no class for the type and shows
// the items the server loads.
public static class ShopShowItemRules {
    // The item ids of the shop that no running row names but some row does, at localNow.
    public static IReadOnlySet<int> Hidden(IEnumerable<GameEventMetadata> events, int shopId, DateTime localNow) {
        var named = new HashSet<int>();
        var shown = new HashSet<int>();
        foreach (GameEventMetadata meta in events) {
            if (meta.Data is not NpcShopShowItem row || row.ShopId != shopId) {
                continue;
            }

            named.UnionWith(row.ItemIds);
            if (new GameEvent(meta).IsActive(localNow)) {
                shown.UnionWith(row.ItemIds);
            }
        }
        named.ExceptWith(shown);
        return named;
    }

    public static IEnumerable<ShopItemMetadata> Filter(IEnumerable<ShopItemMetadata> items, IReadOnlySet<int> hidden) {
        return hidden.Count == 0 ? items : items.Where(item => !hidden.Contains(item.ItemId));
    }
}
