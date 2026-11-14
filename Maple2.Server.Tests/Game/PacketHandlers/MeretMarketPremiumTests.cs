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

// MeretMarket 30 for a premium (custom shop) product: what is paid, from which wallet, what is given, and the
// trophies it counts. The product ids are the KR package trophies' (limited_bundle_buy), the rest made up.
public class MeretMarketPremiumTests {
    private const int ItemId = 20000001;
    private const int Design = 11300001;
    private const int Town = 90000100;
    private const int Package = 3000147; // trophy 92000104's code
    private const int OtherPackage = 3000148; // trophy 92000105's code
    private const int BigPack = 3000900; // Package's additionalQuantity child
    private const int Trophy = 92000104;
    private const int OtherTrophy = 92000105;

    private TestGameSession test = null!;
    private GameSession session => test.Session;

    [SetUp]
    public void SetUp() {
        DbContextOptions options = new DbContextOptionsBuilder()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        test = new TestGameSession(options);
        // The KR rows: one grade, the condition limited_bundle_buy (110) with the product as its code and goal 1.
        test.AddAchievement(TestGameSession.Trophy(Trophy, ConditionType.limited_bundle_buy, [1], codes: [Package]),
            TestGameSession.Trophy(OtherTrophy, ConditionType.limited_bundle_buy, [1], codes: [OtherPackage]));
        test.SetTable(new UgcDesignTable(new Dictionary<int, UgcDesignTable.Entry> {
            [Design] = new(1, MeretMarketCurrencyType.Meret, 390, 0, 0),
        }));
        test.Register(TestGameSession.Metadata(ItemId, slotMax: 1000), TestGameSession.Metadata(Design, EquipSlot.CP, slotMax: 1));
        test.Start();
        test.EnterMap(TestGameSession.Map(Town));
        session.Currency.Meret = 5000;
        session.Currency.GameMeret = 5000;
        session.Currency.Meso = 5000;
        test.TakeSent();
    }

    private static MeretMarketItemMetadata Row(int id, int parentId = 0, int quantity = 1, int bonus = 0, long price = 100, long salePrice = 0,
                                               MeretMarketCurrencyType currency = MeretMarketCurrencyType.Meret, long saleStart = 0, long saleEnd = 0,
                                               JobFilterFlag job = (JobFilterFlag) ~0, short minLevel = 0, short maxLevel = 0, int achievement = 0,
                                               int days = 0) {
        return new MeretMarketItemMetadata(id, parentId, 1, string.Empty, MeretMarketBannerTag.None, ItemId, 1, quantity, bonus, days,
            MeretMarketItemSaleTag.None, currency, price, salePrice, saleStart, saleEnd, job, false, minLevel, maxLevel, achievement, 0, false, false,
            false, string.Empty, 0, 0);
    }

    private void SetMarket(params MeretMarketItemMetadata[] rows) {
        object server = typeof(GameSession).GetField("server", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;
        var table = new MeretMarketTable(rows.ToDictionary(row => row.Id));
        typeof(Maple2.Server.Game.GameServer).GetField("premiumMarketCache", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(server, Maple2.Server.Game.GameServer.PremiumMarket(table, id => session.ItemMetadata.TryGet(id, out ItemMetadata? item) ? item : null));
    }

    // BuyPacket::Write: kind, the product, the design, flag, the child product, a long, index, count, shop id, the gift
    // fields, the price the client shows, a flag and the pay type (3, merets), then four ints.
    private void Buy(int premiumId, int childId = 0, long ugcId = 0, bool gift = false, string receiver = "", long shownPrice = 1) {
        new MeretMarketHandler { TableMetadata = test.TableMetadata, ServerTableMetadata = test.ServerTableMetadata }
            .Handle(session, TestGameSession.Packet(writer => {
                writer.WriteByte(30);
                writer.WriteByte(ugcId > 0 ? (byte) 1 : (byte) 0);
                writer.WriteInt(premiumId);
                writer.WriteLong(ugcId);
                writer.WriteInt(0);
                writer.WriteInt(childId);
                writer.WriteLong(0);
                writer.WriteInt(0);
                writer.WriteInt(1);
                writer.WriteInt(0);
                writer.WriteBool(gift);
                writer.WriteUnicodeString(receiver);
                writer.WriteUnicodeString(string.Empty);
                writer.WriteLong(shownPrice);
                writer.WriteBool(false);
                writer.WriteByte(3);
                writer.WriteInt(0);
                writer.WriteInt(0);
                writer.WriteInt(0);
                writer.WriteInt(0);
            }));
    }

    // The 0x1E (bought) or 0x1F (refused, with its code last) answer.
    private (byte Sub, int Code) Answer() {
        byte[] answer = test.TakeSent().Single(p => TestGameSession.Op(p) == SendOp.MeretMarket);
        return (answer[2], answer[2] == 0x1F ? BitConverter.ToInt32(answer, answer.Length - 4) : 0);
    }

    private long Owned() => session.Item.Inventory.Find(ItemId).Sum(item => item.Amount);

    private void AssertNothingPaid() {
        Assert.Multiple(() => {
            Assert.That(session.Currency.Meret, Is.EqualTo(5000));
            Assert.That(session.Currency.GameMeret, Is.EqualTo(5000));
            Assert.That(session.Currency.Meso, Is.EqualTo(5000));
            Assert.That(Owned(), Is.Zero);
        });
    }

    // limited_bundle_buy counts the product bought: the package's trophy completes, the other package's does not move.
    // Before, nothing raised it and the nine KR package trophies could not be had.
    [Test]
    public void BuyingAPackage_CompletesItsTrophy() {
        SetMarket(Row(Package), Row(OtherPackage));

        Buy(Package);

        Assert.Multiple(() => {
            Assert.That(Answer().Sub, Is.EqualTo(0x1E));
            Assert.That(session.Achievement.HasAchievement(Trophy), Is.True);
            Assert.That(test.TrophyCounter(OtherTrophy) ?? 0, Is.Zero);
        });
    }

    // A refused buy counts nothing, and neither does a design.
    [Test]
    public void ARefusedBuy_OrADesign_CountsNothing() {
        SetMarket(Row(Package, price: 6000));
        Buy(Package);
        Assert.That(Answer().Code, Is.EqualTo(0x88));

        Buy(0, ugcId: 999999);
        Assert.That(Answer().Code, Is.EqualTo(0x6A));

        Assert.That(test.TrophyCounter(Trophy) ?? 0, Is.Zero);
    }

    // The price is the server's: the sale price when the row has one, else the price; what the client says it shows
    // is not used.
    [TestCase(100L, 0L, 4900L)]
    [TestCase(100L, 70L, 4930L)]
    public void TheRowsPriceIsPaid_NotTheShownOne(long price, long salePrice, long left) {
        SetMarket(Row(Package, price: price, salePrice: salePrice));

        Buy(Package, shownPrice: 1);

        Assert.Multiple(() => {
            Assert.That(Answer().Sub, Is.EqualTo(0x1E));
            Assert.That(session.Currency.Meret, Is.EqualTo(left));
            Assert.That(Owned(), Is.EqualTo(1));
        });
    }

    // The quantity and the bonus quantity are given; a bigger pack (a child row) is the parent's item in its own quantity
    // at its own price, and its own id counts for the trophies.
    [Test]
    public void ABiggerPack_GivesItsQuantityAtItsPrice() {
        SetMarket(Row(Package, quantity: 1, bonus: 1), Row(BigPack, parentId: Package, quantity: 10, bonus: 2, price: 800));

        Buy(Package);
        Assert.That(Answer().Sub, Is.EqualTo(0x1E));
        Assert.That(Owned(), Is.EqualTo(2));

        Buy(Package, childId: BigPack);
        Assert.Multiple(() => {
            Assert.That(Answer().Sub, Is.EqualTo(0x1E));
            Assert.That(Owned(), Is.EqualTo(14));
            Assert.That(session.Currency.Meret, Is.EqualTo(5000 - 100 - 800));
        });
    }

    // An unknown bigger pack is not on sale and costs nothing.
    [Test]
    public void AnUnknownPack_IsNotOnSale() {
        SetMarket(Row(Package), Row(BigPack, parentId: Package));
        Buy(Package, childId: BigPack + 1);
        Assert.That(Answer().Code, Is.EqualTo(0x6A));
        AssertNothingPaid();
    }

    // The row's currency is the wallet: merets, red merets only, or mesos, whatever pay type the client sends.
    [TestCase(MeretMarketCurrencyType.Meret, 4900L, 5000L, 5000L)]
    [TestCase(MeretMarketCurrencyType.RedMeret, 5000L, 4900L, 5000L)]
    [TestCase(MeretMarketCurrencyType.Meso, 5000L, 5000L, 4900L)]
    public void TheRowsCurrency_IsTheWallet(MeretMarketCurrencyType currency, long meret, long red, long meso) {
        SetMarket(Row(Package, currency: currency));

        Buy(Package);

        Assert.Multiple(() => {
            Assert.That(Answer().Sub, Is.EqualTo(0x1E));
            Assert.That(session.Currency.Meret, Is.EqualTo(meret));
            Assert.That(session.Currency.GameMeret, Is.EqualTo(red));
            Assert.That(session.Currency.Meso, Is.EqualTo(meso));
        });
    }

    // Too few red merets or mesos: the generic error with the wallet's notice, nothing taken.
    [TestCase(MeretMarketCurrencyType.RedMeret)]
    [TestCase(MeretMarketCurrencyType.Meso)]
    public void TooLittleInTheWallet_PaysNothing(MeretMarketCurrencyType currency) {
        SetMarket(Row(Package, currency: currency, price: 6000));
        Buy(Package);
        List<byte[]> sent = test.TakeSent();
        byte[] market = sent.Single(p => TestGameSession.Op(p) == SendOp.MeretMarket);
        Assert.Multiple(() => {
            Assert.That(market[2], Is.EqualTo(0x1F));
            Assert.That(sent.Count(p => TestGameSession.Op(p) == SendOp.Notice), Is.EqualTo(1));
        });
        AssertNothingPaid();
    }

    // The sale window: before its start not on sale, after its end expired; inside it bought.
    [Test]
    public void TheSaleWindow_IsChecked() {
        long now = DateTime.Now.ToEpochSeconds();
        SetMarket(Row(Package, saleStart: now + 3600), Row(OtherPackage, saleEnd: now - 3600), Row(BigPack, saleStart: now - 3600, saleEnd: now + 3600));

        Buy(Package);
        Assert.That(Answer().Code, Is.EqualTo(0x6A));
        Buy(OtherPackage);
        Assert.That(Answer().Code, Is.EqualTo(0xAC));
        AssertNothingPaid();

        Buy(BigPack);
        Assert.That(Answer().Sub, Is.EqualTo(0x1E));
    }

    // A bigger pack keeps its own sale window: its parent on sale does not put an ended pack on sale.
    [Test]
    public void ABiggerPacksOwnSaleWindow_IsChecked() {
        long now = DateTime.Now.ToEpochSeconds();
        SetMarket(Row(Package), Row(BigPack, parentId: Package, saleEnd: now - 3600));
        Buy(Package, childId: BigPack);
        Assert.That(Answer().Code, Is.EqualTo(0xAC));
        AssertNothingPaid();
    }

    // The level range, the jobs and the trophy already had refuse the buy before anything is paid.
    [Test]
    public void TheRowsLimits_RefuseBeforePaying() {
        short level = session.Player.Value.Character.Level;
        test.AddAchievement(TestGameSession.Trophy(Trophy + 1000, ConditionType.limited_bundle_buy, [1], codes: [1]));
        session.ConditionUpdate(ConditionType.limited_bundle_buy, codeLong: 1);
        SetMarket(Row(Package, minLevel: (short) (level + 1)), Row(OtherPackage, maxLevel: (short) Math.Max(1, level - 1)),
            Row(BigPack, job: JobFilterFlag.None), Row(BigPack + 1, achievement: Trophy + 1000));

        Buy(Package);
        Assert.That(Answer().Code, Is.EqualTo(0xB4));
        if (level > 1) {
            Buy(OtherPackage);
            Assert.That(Answer().Code, Is.EqualTo(0xB4));
        }
        Buy(BigPack);
        Assert.That(Answer().Code, Is.EqualTo(0xB5));
        Buy(BigPack + 1);
        Assert.That(Answer().Code, Is.EqualTo(0xB1));
        AssertNothingPaid();
    }

    // A product with days lasts that long from the buy.
    [Test]
    public void AProductWithDays_Expires() {
        SetMarket(Row(Package, days: 7));
        Buy(Package);
        Assert.That(Answer().Sub, Is.EqualTo(0x1E));
        Item item = session.Item.Inventory.Find(ItemId).Single();
        Assert.That(item.ExpiryTime, Is.EqualTo(DateTime.Now.AddDays(7).ToEpochSeconds()).Within(5));
    }

    // The client offers a gift only on a design: a premium product sent as a gift is refused and costs nothing.
    [Test]
    public void APremiumGift_IsRefused() {
        SetMarket(Row(Package));
        Buy(Package, gift: true, receiver: "Someone");
        Assert.That(Answer().Code, Is.EqualTo(0xA4));
        AssertNothingPaid();
        Assert.That(test.TrophyCounter(Trophy) ?? 0, Is.Zero);
    }
}
