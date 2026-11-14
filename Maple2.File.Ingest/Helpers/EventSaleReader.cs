using System.Globalization;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;

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

    // value1: the rate in 1/10000 of the events whose client class reads one int (GameEventSale::vfunc_4,
    // commonfiles/gameevent.cpp:1952). A row without a rate above 0 is not stored.
    // The TransferItemBound flag as GameEventTransferItemBound::vfunc_2 reads it: an empty value1 keeps the default
    // true; else ParseBoolW (packetlogger.cpp:183), true when the text holds one of "1oOtT".
    public static TransferItemBound TransferItemBound(string? value1) {
        string text = value1?.Trim() ?? string.Empty;
        return new TransferItemBound(text.Length == 0 || text.IndexOfAny(['1', 'o', 'O', 't', 'T']) >= 0);
    }

    // The black market fee rate: 0..10000 (the client asserts on anything else); else no event.
    public static BlackMarketFeeDiscount? FeeRate(string? value1) {
        return int.TryParse(value1?.Trim(), out int rate) && rate is >= 0 and <= 10000 ? new BlackMarketFeeDiscount(rate) : null;
    }

    public static EventRate? Rate(string? value1) {
        return int.TryParse(value1?.Trim(), out int rate) && rate > 0 ? new EventRate(rate) : null;
    }

    // value1 the kinds (1 a capture, 2 the rest), value2 their rates (GameEventPetMastery::vfunc_2,
    // commonfiles/gameevent.cpp:4019): lists of one length, the last place of a kind counts, other kinds are left out.
    // Lists that differ, a place that is not a number, a negative rate or no positive rate give no event (the client's
    // would add nothing, or take exp away).
    public static PetMastery? PetMastery(string? value1, string? value2) {
        string[] kinds = Split(value1);
        string[] rates = Split(value2);
        if (kinds.Length == 0 || kinds.Length != rates.Length) {
            return null;
        }

        int capture = 0;
        int other = 0;
        for (int i = 0; i < kinds.Length; i++) {
            if (!int.TryParse(kinds[i], out int kind) || !int.TryParse(rates[i], out int rate)) {
                return null;
            }
            if (kind == 1) {
                capture = rate;
            } else if (kind == 2) {
                other = rate;
            }
        }
        if (capture < 0 || other < 0) {
            return null;
        }
        return capture > 0 || other > 0 ? new PetMastery(capture, other) : null;
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

    // value1 the rate, value2 the market categories as a comma list kept as a set, value3 the currency item
    // (GameEventPremiumMarketSale::vfunc_2, commonfiles/gameevent.cpp:1397). A category that is not a number gives no
    // event (chosen here: the client's parse reads it as 0, a sale of every category); so does a row without
    // categories or with a rate outside 1..10000 (the client would show a price below 0 above the full rate). A row
    // without value3 is kept with currency 0, which no product matches, as in the client (KR's three 2016 rows).
    // The client splits the list with CStringT::Tokenize, which skips empty places ("40300,,41000", a trailing
    // comma), so they are skipped here too.
    public static PremiumMarketSale? PremiumMarket(string? value1, string? value2, string? value3) {
        if (!int.TryParse(value1?.Trim(), out int rate) || rate is <= 0 or > EventSaleRules.FullRate) {
            return null;
        }
        var ids = new SortedSet<int>();
        foreach (string place in Split(value2).Where(place => place.Length > 0)) {
            if (!int.TryParse(place, out int id)) {
                return null;
            }
            ids.Add(id);
        }
        if (ids.Count == 0) {
            return null;
        }
        int currency = 0;
        if (!string.IsNullOrWhiteSpace(value3) && !int.TryParse(value3.Trim(), out currency)) {
            return null;
        }
        return new PremiumMarketSale(rate, ids.ToArray(), currency);
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
