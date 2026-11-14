using Maple2.File.Ingest.Mapper;
using Maple2.File.Parser.Xml.Table.Server;
using Maple2.Model.Enum;
using Maple2.Model.Game.Shop;
using Maple2.Model.Metadata;

namespace Maple2.Server.Tests.Ingest;

// The shop rows of shop_game_info (ServerTableMapper.MapShop).
public class ShopIngestTests {
    // useLuaResetPayment turns on the restock price tiers (Shop.RestockCost), as the client reads it into the
    // multiplier flag; a shop without it keeps the table's price. Before, the flag was always off.
    [TestCase(true)]
    [TestCase(false)]
    public void MapShop_ReadsTheTieredRestockFlag(bool tiered) {
        ShopMetadata shop = ServerTableMapper.MapShop(new ShopGameInfo {
            shopID = 900001,
            resetEnable = true,
            resetType = 1,
            resetPaymentType = 5,
            resetPrice = 300,
            useLuaResetPayment = tiered,
        });

        Assert.That(shop.RestockData.EnablePriceMultiplier, Is.EqualTo(tiered));
        var instanced = new Shop(shop) { RestockCount = 5 };
        Assert.That(instanced.RestockCost, Is.EqualTo(tiered ? (ShopCurrencyType.Rue, 10) : (ShopCurrencyType.Rue, 300)));
    }
}
