using System.Globalization;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;

namespace Maple2.File.Ingest.Helpers;

// The values of the SaleUgcDesign, SaleHousing, FindDungeonHelperBonus, SaleRemake, SaleBeautyShop, SAHotTime and
// NpcShopShowItem rows. A row that changes nothing (no rate, no cube row, no bonus, no item) is not stored.
public static class EventSaleReader {
    // value1: the rate (GameEventSale, commonfiles/gameevent.cpp:1952).
    public static SaleUgcDesign? UgcDesign(string? value1) {
        return int.TryParse(value1?.Trim(), out int rate) && rate > 0 ? new SaleUgcDesign(rate) : null;
    }

    // value1 the filters, value2 the money types, value3 the rates, comma lists (GameEventSaleHousing,
    // commonfiles/gameevent.cpp:2395). Lists of different lengths or with a place that is not a number give no
    // event: the client asserts on them.
    public static SaleHousing? Housing(string? value1, string? value2, string? value3) {
        string[] filters = Split(value1);
        string[] types = Split(value2);
        string[] rates = Split(value3);
        if (filters.Length == 0 || filters.Length != types.Length || filters.Length != rates.Length) {
            return null;
        }

        var cubes = new SaleHousingCube[filters.Length];
        for (int i = 0; i < filters.Length; i++) {
            if (!short.TryParse(filters[i], out short filter) || !short.TryParse(types[i], out short type) || !int.TryParse(rates[i], out int rate)) {
                return null;
            }
            cubes[i] = new SaleHousingCube(filter, type, rate);
        }
        return new SaleHousing(cubes);
    }

    // value1: the bonus (GameEventFindDungeonHelperBonus, commonfiles/gameevent.cpp:4560).
    public static FindDungeonHelperBonus? HelperBonus(string? value1) {
        return int.TryParse(value1?.Trim(), out int bonus) && bonus > 0 ? new FindDungeonHelperBonus(bonus) : null;
    }

    // value1: the rate, value2: the item class, 0 when empty (GameEventSaleRemake, commonfiles/gameevent.cpp:1966).
    public static SaleRemake? Remake(string? value1, string? value2) {
        if (!int.TryParse(value1?.Trim(), out int rate) || rate <= 0) {
            return null;
        }
        byte type = 0;
        if (!string.IsNullOrWhiteSpace(value2) && !byte.TryParse(value2.Trim(), out type)) {
            return null;
        }
        return new SaleRemake(rate, type);
    }

    // value1 the exchange scroll table rows, a comma list, value2 the rate (GameEventExchangeScrollSale,
    // commonfiles/gameevent.cpp:6313). A place that is not a number gives no event (chosen here: the client's
    // ParseIntArray reads it as 0, a row no scroll names); so does a row without ids or rate.
    public static ExchangeScrollSale? ExchangeScroll(string? value1, string? value2) {
        if (!int.TryParse(value2?.Trim(), out int rate) || rate <= 0) {
            return null;
        }
        string[] places = Split(value1);
        var ids = new int[places.Length];
        for (int i = 0; i < places.Length; i++) {
            if (!int.TryParse(places[i], out ids[i])) {
                return null;
            }
        }
        return ids.Length == 0 ? null : new ExchangeScrollSale(rate, ids);
    }

    // value1 the special attribute ids, value2 their values, comma lists (GameEventSAHotTime,
    // commonfiles/gameevent.cpp:664). The client keeps a value of an attribute whose value type is a rate
    // (specialAbilityStringTable.xml valueType 2, isRate) as (int) (value * 1000) and any other as (int) value,
    // and drops both lists when their lengths differ. A row that adds nothing is not stored.
    public static SAHotTime? HotTime(string? value1, string? value2, Func<int, bool> isRate) {
        string[] ids = Split(value1);
        string[] values = Split(value2);
        if (ids.Length == 0 || ids.Length != values.Length) {
            return null;
        }

        var bonus = new List<SAHotTimeBonus>();
        for (int i = 0; i < ids.Length; i++) {
            if (!int.TryParse(ids[i], out int id) || id is < 0 or > byte.MaxValue || !Enum.IsDefined((SpecialAttribute) id)
                || !float.TryParse(values[i], NumberStyles.Float, CultureInfo.InvariantCulture, out float value)) {
                return null;
            }
            int amount = isRate(id) ? (int) (value * 1000f) : (int) value;
            if (amount != 0) {
                bonus.Add(new SAHotTimeBonus((SpecialAttribute) id, amount));
            }
        }
        return bonus.Count == 0 ? null : new SAHotTime(bonus.ToArray());
    }

    // value1: the beauty shop, value2: the rate, value3: the flag, 0 when empty (GameEventSaleBeautyShop,
    // commonfiles/gameevent.cpp:760).
    public static SaleBeautyShop? BeautyShop(string? value1, string? value2, string? value3) {
        if (!int.TryParse(value1?.Trim(), out int shop) || shop <= 0 || !int.TryParse(value2?.Trim(), out int rate) || rate <= 0) {
            return null;
        }
        int flag = 0;
        if (!string.IsNullOrWhiteSpace(value3) && !int.TryParse(value3.Trim(), out flag)) {
            return null;
        }
        return new SaleBeautyShop(shop, rate, flag != 0);
    }

    // value1 the shop id, value2 its item ids (NpcShopShowItem, server only). A row without a shop or with no item, or
    // with an item that is not a number, is not stored: it would empty the shop.
    public static NpcShopShowItem? ShopShowItem(string? value1, string? value2) {
        if (!int.TryParse(value1?.Trim(), out int shopId) || shopId <= 0) {
            return null;
        }

        string[] values = Split(value2);
        var itemIds = new int[values.Length];
        for (int i = 0; i < values.Length; i++) {
            if (!int.TryParse(values[i], out itemIds[i]) || itemIds[i] <= 0) {
                return null;
            }
        }
        return itemIds.Length > 0 ? new NpcShopShowItem(shopId, itemIds) : null;
    }

    private static string[] Split(string? list) {
        return string.IsNullOrWhiteSpace(list) ? [] : list.Split(',').Select(part => part.Trim()).ToArray();
    }
}
