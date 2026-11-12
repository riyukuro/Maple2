using Maple2.Model.Metadata;

namespace Maple2.File.Ingest.Helpers;

// The values of the SaleUgcDesign, SaleHousing and FindDungeonHelperBonus rows. A row that changes nothing (no
// rate, no cube row, no bonus) is not stored.
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

    private static string[] Split(string? list) {
        return string.IsNullOrWhiteSpace(list) ? [] : list.Split(',').Select(part => part.Trim()).ToArray();
    }
}
