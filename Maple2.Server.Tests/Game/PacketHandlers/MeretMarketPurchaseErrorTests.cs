using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Maple2.Database.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// MeretMarket 30 refused: the 0x1F answer carries the code CMeratMarketMan::GetPurchaseErrorText
// (meratmarketman.cpp:1630) has a text for, and nothing is paid. The ids are made up.
public class MeretMarketPurchaseErrorTests {
    private const int Design = 11300001;
    private const int Town = 90000100;
    private const int Product = 5000001;

    private TestGameSession test = null!;
    private GameSession session => test.Session;

    [SetUp]
    public void SetUp() {
        DbContextOptions options = new DbContextOptionsBuilder()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        test = new TestGameSession(options);
        test.SetTable(new UgcDesignTable(new Dictionary<int, UgcDesignTable.Entry> {
            [Design] = new(1, MeretMarketCurrencyType.Meret, 390, 0, 0),
        }));
        test.Register(TestGameSession.Metadata(Design, EquipSlot.CP, slotMax: 1));
        test.Start();
        test.EnterMap(TestGameSession.Map(Town));
        session.Currency.Meret = 5000;
        test.TakeSent();
    }

    private long List(long sellerAccountId, UgcMarketListingStatus status = UgcMarketListingStatus.Active, long? endTime = null, long price = 3000) {
        session.ItemMetadata.TryGet(Design, out ItemMetadata? metadata);
        using GameStorage.Request db = test.Storage.Context();
        return db.CreateUgcMarketItem(new UgcMarketItem(metadata!) {
            Price = price,
            TabId = 1,
            Status = status,
            ListingEndTime = endTime ?? DateTimeOffset.UtcNow.AddDays(7).ToUnixTimeSeconds(),
            PromotionEndTime = 0,
            SellerAccountId = sellerAccountId,
            SellerCharacterId = 1,
            SellerCharacterName = "Seller",
            Description = string.Empty,
            Tags = [],
            Look = new UgcItemLook { Name = "Design", Url = "item/ugc/design.png" },
            Blueprint = new ItemBlueprint(),
        })!.Id;
    }

    private void SetPremium(long saleStart, long saleEnd, long price = 100) {
        var entry = new MeretMarketItemMetadata(Product, 0, 1, string.Empty, MeretMarketBannerTag.None, Design, 1, 1, 0, 0,
            MeretMarketItemSaleTag.None, MeretMarketCurrencyType.Meret, price, 0, saleStart, saleEnd, (JobFilterFlag) ~0,
            false, 0, 0, 0, 0, false, false, false, string.Empty, 0, 0);
        object server = typeof(GameSession).GetField("server", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;
        var table = new MeretMarketTable(new Dictionary<int, MeretMarketItemMetadata> { [Product] = entry });
        typeof(Maple2.Server.Game.GameServer).GetField("premiumMarketCache", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(server, Maple2.Server.Game.GameServer.PremiumMarket(table, id => session.ItemMetadata.TryGet(id, out ItemMetadata? item) ? item : null));
    }

    // BuyPacket::Write without the gift: kind, the product key, flag, child id, a long, index, count, shop id, the
    // gift fields, the price, a flag and the pay type (3, merets), then four ints.
    private void Buy(int premiumId, long ugcId) {
        new MeretMarketHandler { TableMetadata = test.TableMetadata, ServerTableMetadata = test.ServerTableMetadata }
            .Handle(session, TestGameSession.Packet(writer => {
                writer.WriteByte(30);
                writer.WriteByte(ugcId > 0 ? (byte) 1 : (byte) 0);
                writer.WriteInt(premiumId);
                writer.WriteLong(ugcId);
                writer.WriteInt(0);
                writer.WriteInt(0);
                writer.WriteLong(0);
                writer.WriteInt(0);
                writer.WriteInt(1);
                writer.WriteInt(0);
                writer.WriteBool(false);
                writer.WriteUnicodeString(string.Empty);
                writer.WriteUnicodeString(string.Empty);
                writer.WriteLong(100);
                writer.WriteBool(false);
                writer.WriteByte(3);
                writer.WriteInt(0);
                writer.WriteInt(0);
                writer.WriteInt(0);
                writer.WriteInt(0);
            }));
    }

    private void AssertRefused(int code, long merets = 5000) {
        List<byte[]> sent = test.TakeSent();
        List<byte[]> market = sent.Where(p => TestGameSession.Op(p) == SendOp.MeretMarket).ToList();
        Assert.Multiple(() => {
            Assert.That(market, Has.Count.EqualTo(1));
            Assert.That(market[0][2], Is.EqualTo(0x1F));
            Assert.That(BitConverter.ToInt32(market[0], market[0].Length - 4), Is.EqualTo(code));
            Assert.That(sent.Where(p => TestGameSession.Op(p) == SendOp.Notice), Is.Empty, "no second box");
            Assert.That(session.Currency.Meret, Is.EqualTo(merets));
        });
    }

    [TestCase(0L, 0L, 1000L, 0)]
    [TestCase(900L, 1100L, 1000L, 0)]
    [TestCase(1000L, 1000L, 1000L, 0)]
    [TestCase(1001L, 0L, 1000L, MeretMarketHandler.PurchaseErrorNotSale)]
    [TestCase(0L, 999L, 1000L, MeretMarketHandler.PurchaseErrorNotSaleExpired)]
    public void SaleError_ByTheSaleWindow(long start, long end, long now, int expected) {
        Assert.That(MeretMarketHandler.SaleError(start, end, now), Is.EqualTo(expected));
    }

    [Test]
    public void Premium_UnknownProductIsNotOnSale() {
        SetPremium(0, 0);
        Buy(Product + 1, 0);
        AssertRefused(0x6A);
    }

    [Test]
    public void Premium_SaleNotStartedIsNotOnSale() {
        SetPremium(DateTime.Now.AddDays(1).ToEpochSeconds(), 0);
        Buy(Product, 0);
        AssertRefused(0x6A);
    }

    [Test]
    public void Premium_SaleEndedIsExpired() {
        SetPremium(0, DateTime.Now.AddDays(-1).ToEpochSeconds());
        Buy(Product, 0);
        AssertRefused(0xAC);
    }

    [Test]
    public void Premium_TooFewMeretsIsTheChargeError() {
        SetPremium(0, 0, price: 6000);
        Buy(Product, 0);
        AssertRefused(0x88);
    }

    [Test]
    public void Premium_OnSaleIsBought() {
        SetPremium(0, 0);
        Buy(Product, 0);
        byte[] answer = test.TakeSent().Single(p => TestGameSession.Op(p) == SendOp.MeretMarket);
        Assert.That(answer[2], Is.EqualTo(0x1E));
        Assert.That(session.Currency.Meret, Is.EqualTo(4900));
    }

    [Test]
    public void Design_OwnListingIsRefused() {
        long id = List(session.AccountId);
        Buy(0, id);
        AssertRefused(0x90);
    }

    [Test]
    public void Design_UnknownOrOffSaleListingIsNotOnSale() {
        Buy(0, 999999);
        AssertRefused(0x6A);

        long id = List(session.AccountId + 1, UgcMarketListingStatus.Expired);
        Buy(0, id);
        AssertRefused(0x6A);
    }

    [Test]
    public void Design_PastItsListingTimeIsExpired() {
        long id = List(session.AccountId + 1, endTime: DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds());
        Buy(0, id);
        AssertRefused(0xAC);
    }

    [Test]
    public void Design_TooFewMeretsIsTheChargeError() {
        long id = List(session.AccountId + 1, price: 6000);
        Buy(0, id);
        AssertRefused(0x88);
    }
}
