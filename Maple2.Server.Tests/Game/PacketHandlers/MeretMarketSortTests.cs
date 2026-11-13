using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.PacketHandlers;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The search sort (MeretMarketSearch.SortBy). The client names 5 ChartOrder (asbilldata.cpp:32): the shop table's
// order, which for shop_merat_custom is its id order; 1 (Buy) and 6 (BuyTotal) go by sales. The ids are made up.
public class MeretMarketSortTests {
    private static PremiumMarketItem Premium(int id, int sales) {
        var metadata = new MeretMarketItemMetadata(id, 0, 1, string.Empty, default, 11300010 + id, 1, 1, 0, 0,
            MeretMarketItemSaleTag.None, MeretMarketCurrencyType.Meret, 100, 100, 0, 0, JobFilterFlag.None, false, 0, 0,
            0, 0, false, false, false, string.Empty, 0, 0);
        return new PremiumMarketItem(metadata, TestGameSession.Metadata(11300010 + id)) { SalesCount = sales };
    }

    private static UgcMarketItem Design(long id, int sales) =>
        new(TestGameSession.Metadata(11300100)) { Id = id, SalesCount = sales, Status = UgcMarketListingStatus.Active, ListingEndTime = 0, PromotionEndTime = 0, SellerAccountId = 1, SellerCharacterId = 1, Look = new UgcItemLook(), Blueprint = new ItemBlueprint(),
            SellerCharacterName = string.Empty, Description = string.Empty, Tags = [] };

    private static object[] Order(MeretMarketSort sort, params MarketItem[] items) =>
        MeretMarketHandler.Sort(items, sort, false, false)
            .Select(item => item is PremiumMarketItem premium ? (object) premium.Id : ((UgcMarketItem) item).Id).ToArray();

    [Test]
    public void ChartOrderIsTheTableOrder() {
        Assert.That(Order(MeretMarketSort.MostPopularPremium, Premium(30, 9), Premium(10, 1), Premium(20, 5)),
            Is.EqualTo(new object[] { 10, 20, 30 }));
    }

    [Test]
    public void ChartOrderPutsDesignsAfterByTheirSales() {
        Assert.That(Order(MeretMarketSort.MostPopularPremium, Design(7, 1), Premium(20, 0), Design(8, 4), Premium(10, 0)),
            Is.EqualTo(new object[] { 10, 20, 8L, 7L }));
    }

    [TestCase(MeretMarketSort.MostPopularUgc)]
    [TestCase(MeretMarketSort.TopSeller)]
    public void SalesSortsGoBySales(MeretMarketSort sort) {
        Assert.That(Order(sort, Premium(10, 1), Premium(20, 5), Premium(30, 3)), Is.EqualTo(new object[] { 20, 30, 10 }));
    }
}
