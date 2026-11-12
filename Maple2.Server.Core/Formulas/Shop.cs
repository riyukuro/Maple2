using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;

namespace Maple2.Server.Core.Formulas;

public static class Shop {
    // Client: CItemData::GetSellPrice (commonfiles/itemdata.cpp:1508). The item's price of the rarity; for
    // equipment (property type 1) without a skin type, a price of 0 at level limit 57 and up is the constant
    // SellPrice<Rarity>Max, and every equipment price is multiplied in float by the multicontrol
    // equipmentSellPrice factor of the rarity (1 where the locale's table has no row) and cut to a whole number.
    public static long SellPrice(ItemMetadata metadata, int rarity, ConstantsTable constants) {
        long price = ListPrice(metadata, rarity);
        if (metadata.Property.Type != 1) {
            return price;
        }

        if (metadata.Property.SkinType == 0 && price == 0 && metadata.Limit.Level >= 57) {
            price = constants.SellPriceMax(rarity);
        }
        return (long) ((float) price * constants.EquipmentSellPriceFactor(rarity));
    }

    // The client's item data has one price list; the server data has two (price, priceCustom). Chosen here:
    // priceCustom where it is above 0, else price, which gives the prices the client's own item data lists.
    // A rarity outside the list has no price.
    private static long ListPrice(ItemMetadata metadata, int rarity) {
        long[] custom = metadata.Property.CustomSellPrices;
        long[] prices = metadata.Property.SellPrices;
        if (rarity < 1) {
            return 0;
        }
        if (rarity <= custom.Length && custom[rarity - 1] > 0) {
            return custom[rarity - 1];
        }
        return rarity <= prices.Length ? prices[rarity - 1] : 0;
    }

    public static (ShopCurrencyType CurrencyType, int Cost) ExcessRestockCost(ShopCurrencyType excessCurrencyType, int restockCount) {
        return restockCount switch {
            > 0 and <= 5 => (Constant.InitialTierExcessRestockCurrency, 50000),
            6 => (excessCurrencyType, 10),
            7 => (excessCurrencyType, 20),
            8 => (excessCurrencyType, 40),
            9 => (excessCurrencyType, 60),
            10 => (excessCurrencyType, 80),
            > 10 and <= 15 => (excessCurrencyType, 100),
            _ => (excessCurrencyType, 150),
        };
    }
}
