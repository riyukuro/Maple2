using System;
using System.Collections.Generic;
using System.Reflection;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game;

// A design sold while its seller is online: the sale is stored by the buyer's session, the seller's session holds
// the listings as they were at its login and saves them at logout and when the sales tab opens.
public class UgcMarketSaleTests {
    private static UgcMarketItem List(TestGameSession test, long price) {
        ItemMetadata metadata = TestGameSession.Metadata(11300001, EquipSlot.CP, slotMax: 1);
        test.Register(metadata);
        using GameStorage.Request db = test.Storage.Context();
        return db.CreateUgcMarketItem(new UgcMarketItem(metadata) {
            Price = price,
            TabId = 1,
            Status = UgcMarketListingStatus.Active,
            ListingEndTime = DateTimeOffset.UtcNow.AddDays(7).ToUnixTimeSeconds(),
            PromotionEndTime = 0,
            SellerAccountId = test.AccountId,
            SellerCharacterId = test.CharacterId,
            SellerCharacterName = "Seller",
            Description = string.Empty,
            Tags = [],
            Look = new UgcItemLook { Name = "Design" },
            Blueprint = new ItemBlueprint(),
        }) ?? throw new InvalidOperationException("listing not created");
    }

    // The seller's login: the market manager reads the account's listings (and the favourite designers, none).
    private static GameSession Login(TestGameSession test) {
        GameSession session = test.Start();
        typeof(ConfigManager).GetField("favoriteDesigners", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(session.Config, new List<long>());
        session.UgcMarket = new UgcMarketManager(session);
        return session;
    }

    private static SoldUgcMarketItem Sell(TestGameSession test, long id) {
        using GameStorage.Request db = test.Storage.Context();
        UgcMarketItem listing = db.GetUgcMarketItem(id) ?? throw new InvalidOperationException("listing not found");
        return UgcMarketManager.RecordSale(db, listing) ?? throw new InvalidOperationException("sale not stored");
    }

    private static long StoredTotal(TestGameSession test) {
        using GameStorage.Request db = test.Storage.Context();
        return UgcMarketManager.SalesTotal(db.GetUgcListingsByAccountId(test.AccountId).Values);
    }

    // Two sales at 3,000 while the seller is online, then the seller's save: the total stays 6,000, so the third
    // sale is on the second step (60%).
    [Test]
    public void SellerSave_KeepsTheSalesOfTheBuyers() {
        var test = new TestGameSession();
        long id = List(test, 3000).Id;
        GameSession seller = Login(test);

        Assert.That(Sell(test, id).Profit, Is.EqualTo(2100));
        Assert.That(Sell(test, id).Profit, Is.EqualTo(2100));
        using (GameStorage.Request db = test.Storage.Context()) {
            seller.UgcMarket.Save(db);
        }

        Assert.Multiple(() => {
            Assert.That(StoredTotal(test), Is.EqualTo(6000));
            Assert.That(Sell(test, id).Profit, Is.EqualTo(1800));
            Assert.That(StoredTotal(test), Is.EqualTo(9000));
        });
    }

    // A sale carries its time: the client shows it as the sale's date and counts the payment's wait from it,
    // and the profit cannot be collected before that wait is over.
    [Test]
    public void Sale_IsStoredWithItsTime() {
        var test = new TestGameSession();
        long id = List(test, 3000).Id;
        GameSession seller = Login(test);
        long before = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        SoldUgcMarketItem sold = Sell(test, id);
        seller.UgcMarket.RefreshSales();
        seller.UgcMarket.CollectProfit(sold.Id);

        using GameStorage.Request db = test.Storage.Context();
        SoldUgcMarketItem stored = db.GetMySoldUgcListings(test.AccountId)[sold.Id];
        Assert.Multiple(() => {
            Assert.That(sold.SoldTime, Is.InRange(before - 1, before + 60));
            Assert.That(stored.SoldTime, Is.InRange(before - 1, before + 60));
        });
    }

    // A listing row whose blueprint column holds the column default `{}` is written to the client with an
    // empty builder name.
    [Test]
    public void Blueprint_OfTheColumnDefaultIsWritten() {
        Maple2.Database.Model.ItemBlueprint stored =
            System.Text.Json.JsonSerializer.Deserialize<Maple2.Database.Model.ItemBlueprint>("{}")!;
        ItemBlueprint blueprint = stored;

        var writer = new Maple2.PacketLib.Tools.ByteWriter();
        Assert.DoesNotThrow(() => blueprint.WriteTo(writer));
        Assert.That(writer.Length, Is.EqualTo(8 + 12 + 8 + 4 + 16 + 2));
    }

    // The sales tab (which saves) and taking the listing off the market leave the count, and the seller's own copy
    // follows the stored count.
    [Test]
    public void SellerRefreshAndUnlist_KeepTheSalesOfTheBuyers() {
        var test = new TestGameSession();
        long id = List(test, 3000).Id;
        GameSession seller = Login(test);
        Sell(test, id);
        Sell(test, id);

        seller.UgcMarket.RefreshSales();
        Assert.That(seller.UgcMarket.TryGetItem(id, out UgcMarketItem? held), Is.True);
        int heldCount = held!.SalesCount;
        Sell(test, id);
        seller.UgcMarket.UnlistItem(id);

        Assert.Multiple(() => {
            Assert.That(heldCount, Is.EqualTo(2));
            Assert.That(StoredTotal(test), Is.EqualTo(9000));
            Assert.That(test.Errors, Is.Empty);
        });
    }
}
