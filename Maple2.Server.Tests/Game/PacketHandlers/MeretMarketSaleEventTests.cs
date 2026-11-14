using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Maple2.File.Ingest.Helpers;
using Maple2.Model.Enum;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The meret market sale (PremiumMarketSale): the event list entry, the row reader, the price the client shows and
// sends (CDBPremiumProduct::GetSalePrice) and the price MeretMarket 30 charges. The ids are made up.
public class MeretMarketSaleEventTests {
    private const int Item = 11300001;
    private const int Town = 90000100;
    private const int Product = 5000001;
    private const int Category = 40310;

    private TestGameSession test = null!;
    private GameSession session => test.Session;

    private void Start(params (int Id, PremiumMarketSale Sale, bool Ended)[] sales) {
        DbContextOptions options = new DbContextOptionsBuilder()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        test = new TestGameSession(options);
        test.Register(TestGameSession.Metadata(Item, EquipSlot.CP, slotMax: 1));
        foreach ((int id, PremiumMarketSale sale, bool ended) in sales) {
            test.AddEvent(id, GameEventType.PremiumMarketSale, sale, end: ended ? DateTime.Now.AddDays(-1) : null);
        }
        test.Start();
        test.EnterMap(TestGameSession.Map(Town));
        session.Currency.Meret = 5000;
        session.Currency.GameMeret = 5000;
        test.TakeSent();
    }

    private void SetPremium(MeretMarketCurrencyType currency, long price, long salePrice, int tabId = Category) {
        var entry = new MeretMarketItemMetadata(Product, 0, tabId, string.Empty, MeretMarketBannerTag.None, Item, 1, 1, 0, 0,
            MeretMarketItemSaleTag.None, currency, price, salePrice, 0, 0, (JobFilterFlag) ~0,
            false, 0, 0, 0, 0, false, false, false, string.Empty, 0, 0);
        object server = typeof(GameSession).GetField("server", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;
        var table = new MeretMarketTable(new Dictionary<int, MeretMarketItemMetadata> { [Product] = entry });
        typeof(Maple2.Server.Game.GameServer).GetField("premiumMarketCache", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(server, Maple2.Server.Game.GameServer.PremiumMarket(table, id => session.ItemMetadata.TryGet(id, out ItemMetadata? item) ? item : null));
    }

    // BuyPacket::Write without the gift, as MeretMarketPurchaseErrorTests sends it (the price field is the client's
    // GetSalePrice; the server does not trust it).
    private void Buy(byte payType = 3) {
        new MeretMarketHandler { TableMetadata = test.TableMetadata, ServerTableMetadata = test.ServerTableMetadata }
            .Handle(session, TestGameSession.Packet(writer => {
                writer.WriteByte(30);
                writer.WriteByte(0);
                writer.WriteInt(Product);
                writer.WriteLong(0);
                writer.WriteInt(0);
                writer.WriteInt(0);
                writer.WriteLong(0);
                writer.WriteInt(0);
                writer.WriteInt(1);
                writer.WriteInt(0);
                writer.WriteBool(false);
                writer.WriteUnicodeString(string.Empty);
                writer.WriteUnicodeString(string.Empty);
                writer.WriteLong(1);
                writer.WriteBool(false);
                writer.WriteByte(payType);
                writer.WriteInt(0);
                writer.WriteInt(0);
                writer.WriteInt(0);
                writer.WriteInt(0);
            }));
    }

    private static PremiumMarketSale Sale(int rate, int currency, params int[] categories) => new(rate, categories, currency);

    private void AssertBought(long merets, long redMerets) {
        byte[] answer = test.TakeSent().Single(p => TestGameSession.Op(p) == SendOp.MeretMarket);
        Assert.Multiple(() => {
            Assert.That(answer[2], Is.EqualTo(0x1E));
            Assert.That(session.Currency.Meret, Is.EqualTo(merets));
            Assert.That(session.Currency.GameMeret, Is.EqualTo(redMerets));
        });
    }

    [Test]
    public void Buy_ChargesTheLoweredSalePrice() {
        Start((7400, Sale(2500, 100000, Category), false));
        SetPremium(MeretMarketCurrencyType.Meret, 1000, 990);
        Buy();
        AssertBought(5000 - 743, 5000); // ceil(990 * 7500 / 10000) = ceil(742.5)
    }

    [Test]
    public void Buy_RedMeretSaleLowersRedMeretProducts() {
        Start((7400, Sale(5000, 100001, 0), false), (7401, Sale(9000, 100000, 0), false));
        SetPremium(MeretMarketCurrencyType.RedMeret, 300, 300);
        Buy(2);
        AssertBought(5000, 5000 - 150);
    }

    [Test]
    public void Buy_HighestCoveringRateWins() {
        Start((7400, Sale(1000, 100000, 0), false), (7401, Sale(4000, 100000, 40300), false),
            (7402, Sale(8000, 100000, 41000), false));
        SetPremium(MeretMarketCurrencyType.Meret, 200, 200);
        Buy();
        AssertBought(5000 - 120, 5000);
    }

    [TestCase(false, 100000, 99999)] // another category
    [TestCase(false, 100001, Category)] // another currency
    [TestCase(false, 0, Category)] // no currency, like KR's 2016 rows
    [TestCase(true, 100000, Category)] // ended
    public void Buy_UncoveredProductPaysTheSalePrice(bool ended, int currency, int category) {
        Start((7400, Sale(5000, currency, category), ended));
        SetPremium(MeretMarketCurrencyType.Meret, 1000, 400);
        Buy();
        AssertBought(5000 - 400, 5000);
    }

    [Test]
    public void Buy_RowWithoutSalePriceKeepsTheListPrice() {
        Start((7400, Sale(5000, 100000, 0), false));
        SetPremium(MeretMarketCurrencyType.Meret, 1000, 0);
        Buy();
        AssertBought(5000 - 1000, 5000);
    }

    [TestCase(1000L, 5000, 500L)]
    [TestCase(999L, 5000, 500L)] // 499.5 rounds up
    [TestCase(1L, 9999, 1L)]
    [TestCase(1000L, 10000, 0L)]
    [TestCase(0L, 5000, 0L)]
    [TestCase(3000000L, 5000, 1500000L)] // the client's 32-bit product would wrap here
    public void Price_IsTheCeilingOfTheRest(long price, int rate, long expected) {
        var events = new List<GameEvent> { new TestGameSession().AddEvent(1, GameEventType.PremiumMarketSale, Sale(rate, 100000, 0)) };
        Assert.That(EventSaleRules.PremiumMarketPrice(price, MeretMarketCurrencyType.Meret, Category, events), Is.EqualTo(expected));
    }

    [Test]
    public void Price_MesoProductsAreNeverLowered() {
        var events = new List<GameEvent> { new TestGameSession().AddEvent(1, GameEventType.PremiumMarketSale, Sale(5000, 0, 0)) };
        Assert.That(EventSaleRules.PremiumMarketPrice(1000, MeretMarketCurrencyType.Meso, Category, events), Is.EqualTo(1000));
    }

    [TestCase(40310, 40300, true)] // the category's top
    [TestCase(40310, 40310, true)]
    [TestCase(40310, 0, true)]
    [TestCase(40300, 40300, true)]
    [TestCase(40300, 40310, false)] // a top does not cover a sub category's row
    [TestCase(40310, 40000, false)]
    [TestCase(0, 40300, false)]
    public void Covers_ByTheCategorySet(int category, int saleCategory, bool expected) {
        Assert.Multiple(() => {
            Assert.That(EventSaleRules.PremiumSaleCovers(Sale(5000, 100000, saleCategory), category, 100000), Is.EqualTo(expected));
            Assert.That(EventSaleRules.PremiumSaleCovers(Sale(5000, 100001, saleCategory), category, 100000), Is.False);
        });
    }

    [Test]
    public void Event_WritesRateCategoriesAndCurrency() {
        GameEvent gameEvent = new TestGameSession().AddEvent(7001, GameEventType.PremiumMarketSale, Sale(5000, 100000, 40300, 41000));
        var writer = new ByteWriter();
        gameEvent.WriteTo(writer);
        var name = new ByteWriter();
        name.WriteUnicodeString("PremiumMarketSale");
        Assert.That(Convert.ToHexString(writer.ToArray()),
            Is.EqualTo(Convert.ToHexString(name.ToArray()) + "591B0000" + "88130000" + "02000000" + "6C9D0000" + "28A00000" + "A0860100"));
    }

    [Test]
    public void Reader_ReadsTheRow() {
        PremiumMarketSale? kr = EventSaleReader.PremiumMarket("5000", "41000,40300", null);
        PremiumMarketSale? own = EventSaleReader.PremiumMarket(" 3000 ", "0", "100001");
        Assert.Multiple(() => {
            Assert.That(kr!.Rate, Is.EqualTo(5000));
            Assert.That(kr.CategoryIds, Is.EqualTo(new[] { 40300, 41000 })); // a set, in order
            Assert.That(kr.Currency, Is.EqualTo(0));
            Assert.That(own!.Rate, Is.EqualTo(3000));
            Assert.That(own.CategoryIds, Is.EqualTo(new[] { 0 }));
            Assert.That(own.Currency, Is.EqualTo(100001));
            Assert.That(EventSaleReader.PremiumMarket("5000", "40300,40300", "100000")!.CategoryIds, Is.EqualTo(new[] { 40300 }));
            Assert.That(EventSaleReader.PremiumMarket("0", "40300", "100000"), Is.Null);
            Assert.That(EventSaleReader.PremiumMarket("10001", "40300", "100000"), Is.Null);
            Assert.That(EventSaleReader.PremiumMarket("5000", "", "100000"), Is.Null);
            Assert.That(EventSaleReader.PremiumMarket("5000", "40300,x", "100000"), Is.Null);
            Assert.That(EventSaleReader.PremiumMarket("5000", "40300", "x"), Is.Null);
        });
    }
}
