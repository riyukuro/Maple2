using Maple2.Model.Enum;

namespace Maple2.Model.Metadata;

public record FurnishingShopTable(IReadOnlyDictionary<int, FurnishingShopTable.Entry> Entries) : Table {
    public record Entry(
        int ItemId,
        bool Buyable,
        FurnishingCurrencyType FurnishingTokenType,
        int Price);

    // A row of the client's price table (CPriceTable::LoadHousing, commonfiles/pricetable.cpp:111): attributes
    // id, ugcHousingBuy, ugcHousingDefaultPrice and ugcHousingMoneyType (1 when the row has none).
    public static Entry? ClientRow(string id, string buy, string price, string moneyType) {
        if (!int.TryParse(id.Trim(), out int itemId)) {
            return null;
        }
        long.TryParse(price.Trim(), out long value);
        if (!int.TryParse(moneyType.Trim(), out int type)) {
            type = 1;
        }
        buy = buy.Trim();
        return new Entry(itemId, buy == "1" || buy.Equals("true", StringComparison.OrdinalIgnoreCase),
            (FurnishingCurrencyType) type, (int) Math.Clamp(value, 0, int.MaxValue));
    }

    // The server pack's rows with the played client's rows over them, in the client's file order (a later row
    // replaces an earlier one). The client shows and checks its own row, so that row is the price.
    public static Dictionary<int, Entry> WithClientRows(IReadOnlyDictionary<int, Entry> server, IEnumerable<Entry> clientRows) {
        var result = new Dictionary<int, Entry>(server);
        foreach (Entry row in clientRows) {
            result[row.ItemId] = row;
        }
        return result;
    }
}
