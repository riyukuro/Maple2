using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.File.Ingest.Helpers;
using Maple2.Model.Enum;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The design price sale, the cube price sale and the dungeon helper bonus: the event list entries, the row readers
// and the prices and counts the server takes from them.
public class EventSaleTypesTests {
    private const int EventId = 7001;

    private static string Written(GameEventType type, GameEventData data) {
        var test = new TestGameSession();
        GameEvent gameEvent = test.AddEvent(EventId, type, data);
        var writer = new ByteWriter();
        gameEvent.WriteTo(writer);
        return Convert.ToHexString(writer.ToArray());
    }

    private static string Name(string name) {
        var writer = new ByteWriter();
        writer.WriteUnicodeString(name);
        return Convert.ToHexString(writer.ToArray());
    }

    [Test]
    public void SaleUgcDesign_WritesTheRate() {
        Assert.That(Written(GameEventType.SaleUgcDesign, new SaleUgcDesign(8000)),
            Is.EqualTo(Name("SaleUgcDesign") + "591B0000" + "401F0000"));
    }

    [Test]
    public void SaleHousing_WritesACountThenFilterTypeRate() {
        var data = new SaleHousing([new SaleHousingCube(100, 1, 8000), new SaleHousingCube(93, 3, 10000)]);
        Assert.That(Written(GameEventType.SaleHousing, data),
            Is.EqualTo(Name("SaleHousing") + "591B0000" + "02000000"
                       + "6400" + "0100" + "401F0000"
                       + "5D00" + "0300" + "10270000"));
    }

    [Test]
    public void FindDungeonHelperBonus_WritesTheBonus() {
        Assert.That(Written(GameEventType.FindDungeonHelperBonus, new FindDungeonHelperBonus(1)),
            Is.EqualTo(Name("FindDungeonHelperBonus") + "591B0000" + "01000000"));
    }

    [TestCase(390, 0, 390)]
    [TestCase(390, 8000, 78)]
    [TestCase(390, 5000, 195)]
    [TestCase(399, 5000, 199)] // whole numbers, rounded down
    [TestCase(390, 10000, 0)]
    [TestCase(390, 12000, 0)]
    [TestCase(390, -5, 390)]
    [TestCase(0, 8000, 0)]
    public void UgcDesignPrice_TakesTheRateOff(long price, int rate, long expected) {
        Assert.That(EventSaleRules.UgcDesignPrice(price, rate), Is.EqualTo(expected));
    }

    private static readonly SaleHousing Sale = new([
        new SaleHousingCube(100, 1, 8000), new SaleHousingCube(100, 3, 5000),
        new SaleHousingCube(93, 1, 10000), new SaleHousingCube(100, 1, 1000),
    ]);

    [TestCase(100, FurnishingCurrencyType.Meso, 500, 100)] // the first row of the filter and type
    [TestCase(100, FurnishingCurrencyType.Meret, 5, 3)] // 2.5 rounds up
    [TestCase(100, FurnishingCurrencyType.Meret, 3, 2)]
    [TestCase(93, FurnishingCurrencyType.Meso, 1000, 0)]
    [TestCase(93, FurnishingCurrencyType.Meret, 10, 10)] // no row of that money type
    [TestCase(90, FurnishingCurrencyType.Meso, 500, 500)] // no row of that filter
    public void HousingPrice_FirstRowOfTheFilterAndMoneyType(int filter, FurnishingCurrencyType currency, int price, int expected) {
        Assert.That(EventSaleRules.HousingPrice(Sale, filter, currency, price), Is.EqualTo(expected));
        Assert.That(EventSaleRules.HousingPrice(null, filter, currency, price), Is.EqualTo(price));
    }

    [Test]
    public void HelperRewardTimes_OncePlusTheBonuses() {
        Assert.That(EventSaleRules.HelperRewardTimes([]), Is.EqualTo(1));
        Assert.That(EventSaleRules.HelperRewardTimes([1]), Is.EqualTo(2));
        Assert.That(EventSaleRules.HelperRewardTimes([1, 2, -4, 0]), Is.EqualTo(4));
        Assert.That(EventSaleRules.HelperRewardTimes([int.MaxValue, int.MaxValue]), Is.EqualTo(EventSaleRules.MaxHelperRewardTimes));
    }

    [Test]
    public void Reader_ReadsTheRows() {
        Assert.That(EventSaleReader.UgcDesign("8000"), Is.EqualTo(new SaleUgcDesign(8000)));
        Assert.That(EventSaleReader.UgcDesign(null), Is.Null);
        Assert.That(EventSaleReader.UgcDesign("0"), Is.Null);
        Assert.That(EventSaleReader.HelperBonus(" 1 "), Is.EqualTo(new FindDungeonHelperBonus(1)));
        Assert.That(EventSaleReader.HelperBonus(""), Is.Null);
        Assert.That(EventSaleReader.HelperBonus("x"), Is.Null);

        SaleHousing? housing = EventSaleReader.Housing("100,200, 93", "1,3,1", "8000,8000,10000");
        Assert.That(housing!.Cubes, Is.EqualTo(new[] {
            new SaleHousingCube(100, 1, 8000), new SaleHousingCube(200, 3, 8000), new SaleHousingCube(93, 1, 10000),
        }));
        Assert.That(EventSaleReader.Housing("100,200", "1", "8000,8000"), Is.Null); // lists of different lengths
        Assert.That(EventSaleReader.Housing("100,x", "1,3", "8000,8000"), Is.Null);
        Assert.That(EventSaleReader.Housing(null, null, null), Is.Null);
    }

    // The day of the week and the part time are tested on the clock the start and end are on (local time).
    [Test]
    public void IsActive_DayAndPartTimeOnTheGivenLocalClock() {
        var now = new DateTime(2030, 6, 15, 0, 30, 0, DateTimeKind.Local); // a Saturday, half past midnight
        var metadata = new GameEventMetadata(EventId, GameEventType.SaleUgcDesign, now.AddDays(-1), now.AddDays(1),
            TimeSpan.Zero, TimeSpan.Zero, [DayOfWeek.Saturday], new SaleUgcDesign(8000), "", "", "", "");
        Assert.That(new GameEvent(metadata).IsActive(now), Is.True);
        Assert.That(new GameEvent(metadata).IsActive(now.AddHours(-1)), Is.False); // Friday 23:30
        Assert.That(new GameEvent(metadata with { ActiveDays = [DayOfWeek.Friday] }).IsActive(now), Is.False);

        GameEventMetadata part = metadata with { ActiveDays = [], StartPartTime = TimeSpan.FromMinutes(20), EndPartTime = TimeSpan.FromMinutes(40) };
        Assert.That(new GameEvent(part).IsActive(now), Is.True);
        Assert.That(new GameEvent(part).IsActive(now.AddMinutes(11)), Is.False);
        Assert.That(new GameEvent(part).IsActive(now.AddMinutes(-11)), Is.False);
        Assert.That(new GameEvent(metadata with { ActiveDays = [] }).IsActive(now.AddDays(2)), Is.False); // ended
    }

    [TestCase(12000, 0)] // held to 10000
    [TestCase(-5000, 500)] // held to 0
    public void HousingPrice_HoldsTheRate(int rate, int expected) {
        var sale = new SaleHousing([new SaleHousingCube(100, 1, rate)]);
        Assert.That(EventSaleRules.HousingPrice(sale, 100, FurnishingCurrencyType.Meso, 500), Is.EqualTo(expected));
    }

    [Test]
    public void SaleRemake_WritesTheRateAndTheClass() {
        Assert.That(Written(GameEventType.SaleRemake, new SaleRemake(3000, 2)),
            Is.EqualTo(Name("SaleRemake") + "591B0000" + "B80B0000" + "02"));
    }

    [Test]
    public void SaleBeautyShop_WritesTheShopTheRateAndTheFlag() {
        Assert.That(Written(GameEventType.SaleBeautyShop, new SaleBeautyShop(500, 5000, false)),
            Is.EqualTo(Name("SaleBeautyShop") + "591B0000" + "F4010000" + "88130000" + "00"));
        Assert.That(Written(GameEventType.SaleBeautyShop, new SaleBeautyShop(508, 3000, true)),
            Is.EqualTo(Name("SaleBeautyShop") + "591B0000" + "FC010000" + "B80B0000" + "01"));
    }

    [Test]
    public void RemakeAndBeautyRows_AreRead() {
        Assert.That(EventSaleReader.Remake("3000", "0"), Is.EqualTo(new SaleRemake(3000, 0)));
        Assert.That(EventSaleReader.Remake(" 5000 ", null), Is.EqualTo(new SaleRemake(5000, 0)));
        Assert.That(EventSaleReader.Remake("5000", "3"), Is.EqualTo(new SaleRemake(5000, 3)));
        Assert.That(EventSaleReader.Remake("0", "0"), Is.Null);
        Assert.That(EventSaleReader.Remake("", "0"), Is.Null);
        Assert.That(EventSaleReader.Remake("3000", "x"), Is.Null);

        Assert.That(EventSaleReader.BeautyShop("500", "5000", null), Is.EqualTo(new SaleBeautyShop(500, 5000, false)));
        Assert.That(EventSaleReader.BeautyShop("508", "3000", "1"), Is.EqualTo(new SaleBeautyShop(508, 3000, true)));
        Assert.That(EventSaleReader.BeautyShop("500", "0", null), Is.Null);
        Assert.That(EventSaleReader.BeautyShop(null, "5000", null), Is.Null);
        Assert.That(EventSaleReader.BeautyShop("500", "5000", "x"), Is.Null);
    }

    [TestCase(1, 13, false, 1)]
    [TestCase(1, 17, false, 1)]
    [TestCase(1, 12, false, 2)]
    [TestCase(1, 18, false, 2)]
    [TestCase(1, 21, false, 2)]
    [TestCase(1, 22, false, 0)]
    [TestCase(3, 0, false, 0)] // a weapon
    [TestCase(6, 0, true, 3)]
    public void RemakeSaleType_NamesTheItemClass(int group, int type, bool isPet, int expected) {
        Assert.That(EventSaleRules.RemakeSaleType(group, type, isPet), Is.EqualTo((byte) expected));
    }

    [Test]
    public void RemakeSaleRate_IsTheHigherOfTheClassAndAll() {
        SaleRemake[] sales = [new(3000, 0), new(5000, 1), new(2000, 2), new(4000, 1)];
        Assert.That(EventSaleRules.RemakeSaleRate(sales, 1), Is.EqualTo(5000));
        Assert.That(EventSaleRules.RemakeSaleRate(sales, 2), Is.EqualTo(3000));
        Assert.That(EventSaleRules.RemakeSaleRate(sales, 3), Is.EqualTo(3000));
        Assert.That(EventSaleRules.RemakeSaleRate(sales, 0), Is.EqualTo(3000));
        Assert.That(EventSaleRules.RemakeSaleRate([], 1), Is.EqualTo(0));
    }

    [TestCase(10, 3000, 0.5f, 7)]
    [TestCase(10, 5000, 0.5f, 5)]
    [TestCase(10, 8000, 0.5f, 5)] // held to the limit
    [TestCase(3, 3000, 0.5f, 2)] // 2.1 cut
    [TestCase(1, 5000, 0.5f, 1)] // at least 1
    [TestCase(10, 3000, 0f, 10)] // a limit of 0: nothing off
    [TestCase(10, 0, 0.5f, 10)]
    [TestCase(0, 3000, 0.5f, 0)]
    public void RemakeIngredientCount_TakesTheRateOffUpToTheLimit(int count, int rate, float limit, int expected) {
        Assert.That(EventSaleRules.RemakeIngredientCount(count, rate, limit), Is.EqualTo(expected));
    }

    private static GameEvent ShopSale(int id, int daysAgo, SaleBeautyShop sale) => new(new GameEventMetadata(id,
        GameEventType.SaleBeautyShop, DateTime.Now.AddDays(-daysAgo), DateTime.Now.AddDays(30), TimeSpan.Zero, TimeSpan.Zero, [],
        sale, string.Empty, string.Empty, string.Empty, string.Empty));

    [Test]
    public void BeautyShopRate_IsTheLastStartedOfTheShop_NotTheHighest() {
        GameEvent[] events = [
            ShopSale(7003, 2, new SaleBeautyShop(500, 3000, false)),
            ShopSale(7001, 5, new SaleBeautyShop(500, 5000, false)),
            ShopSale(7002, 1, new SaleBeautyShop(501, 10000, false)),
        ];
        Assert.That(EventSaleRules.BeautyShopRate(events, 500), Is.EqualTo(3000));
        Assert.That(EventSaleRules.BeautyShopRate(Enumerable.Reverse(events), 500), Is.EqualTo(3000));
        Assert.That(EventSaleRules.BeautyShopRate(events, 501), Is.EqualTo(10000));
        Assert.That(EventSaleRules.BeautyShopRate(events, 504), Is.EqualTo(0));
    }

    [Test]
    public void BeautyShopRate_OfOneStart_IsTheHigherId() {
        DateTime start = DateTime.Now.AddDays(-2);
        GameEvent At(int id, int rate) => new(new GameEventMetadata(id, GameEventType.SaleBeautyShop, start, DateTime.Now.AddDays(30),
            TimeSpan.Zero, TimeSpan.Zero, [], new SaleBeautyShop(500, rate, false), string.Empty, string.Empty, string.Empty, string.Empty));
        Assert.That(EventSaleRules.BeautyShopRate([At(7002, 2000), At(7001, 5000)], 500), Is.EqualTo(2000));
        Assert.That(EventSaleRules.BeautyShopRate([At(7001, 5000), At(7002, 2000)], 500), Is.EqualTo(2000));
    }

    [Test]
    public void BeautyShopRate_LeavesOutAPcRoomRow() {
        // The flagged slot counts only for a PC room player; there is none.
        Assert.That(EventSaleRules.BeautyShopRate([ShopSale(7001, 2, new SaleBeautyShop(500, 5000, true))], 500), Is.EqualTo(0));
        Assert.That(EventSaleRules.BeautyShopRate([
            ShopSale(7001, 5, new SaleBeautyShop(500, 2000, false)),
            ShopSale(7002, 2, new SaleBeautyShop(500, 5000, true)),
        ], 500), Is.EqualTo(2000));
    }

    [TestCase(3000, 0, 3000)]
    [TestCase(0, 300, 3000)] // thousandths: 0.3 of the count
    [TestCase(3000, 400, 4000)]
    [TestCase(5000, 400, 5000)]
    [TestCase(0, 0, 0)]
    [TestCase(3000, -200, 3000)]
    public void RemakeDiscount_IsTheHigherOfTheSaleAndTheFeeAttribute(int saleRate, long feeTotal, int expected) {
        Assert.That(EventSaleRules.RemakeDiscount(saleRate, feeTotal), Is.EqualTo(expected));
    }

    #region SAHotTime
    [Test]
    public void SAHotTime_WritesTheFourValuesAsTheyAre() {
        var gameEvent = new GameEvent(new GameEventMetadata(EventId, GameEventType.SAHotTime, DateTime.Now.AddDays(-3), DateTime.Now.AddDays(30),
            TimeSpan.Zero, TimeSpan.Zero, [], new SAHotTime([new SAHotTimeBonus((SpecialAttribute) 59, 1000)]), "1,59", "1,1", "", "tip"));
        var writer = new ByteWriter();
        gameEvent.WriteTo(writer);
        Assert.That(Convert.ToHexString(writer.ToArray()),
            Is.EqualTo(Name("SAHotTime") + "591B0000" + Name("1,59") + Name("1,1") + Name("") + Name("tip")));
    }

    [Test]
    public void HotTimeReader_KeepsARateInThousandthsAndAnyOtherWhole() {
        SAHotTime? read = EventSaleReader.HotTime("1, 59,61", "2,1,0.1", id => id != 1);
        Assert.That(read, Is.Not.Null);
        Assert.That(read!.Bonus, Is.EqualTo(new[] {
            new SAHotTimeBonus((SpecialAttribute) 1, 2),
            new SAHotTimeBonus((SpecialAttribute) 59, 1000),
            new SAHotTimeBonus((SpecialAttribute) 61, 100),
        }));
    }

    [TestCase("1,59", "1")] // the lists differ in length: the client drops both
    [TestCase("", "")]
    [TestCase(null, null)]
    [TestCase("59", "0")] // nothing added
    [TestCase("59", "x")]
    [TestCase("30000", "1")] // no such attribute
    public void HotTimeReader_DropsARowThatAddsNothing(string? value1, string? value2) {
        Assert.That(EventSaleReader.HotTime(value1, value2, _ => true), Is.Null);
    }

    [Test]
    public void HotTimeReader_CutsAWholeValueBelowOne() {
        Assert.That(EventSaleReader.HotTime("59", "0.1", _ => false), Is.Null);
    }

    [Test]
    public void HotTimeBonus_AddsTheRunningEventsUp() {
        Dictionary<SpecialAttribute, long> sum = EventSaleRules.HotTimeBonus([
            new SAHotTime([new SAHotTimeBonus((SpecialAttribute) 59, 1000), new SAHotTimeBonus((SpecialAttribute) 61, 100)]),
            new SAHotTime([new SAHotTimeBonus((SpecialAttribute) 59, 2000)]),
        ]);
        Assert.That(sum, Has.Count.EqualTo(2));
        Assert.That(sum[(SpecialAttribute) 59], Is.EqualTo(3000));
        Assert.That(sum[(SpecialAttribute) 61], Is.EqualTo(100));
    }

    [Test]
    public void HotTime_RaisesThePlayersTotals_WhileItRuns() {
        var test = new TestGameSession();
        test.AddEvent(7001, GameEventType.SAHotTime, new SAHotTime([new SAHotTimeBonus((SpecialAttribute) 59, 1000)]));
        test.AddEvent(7002, GameEventType.SAHotTime, new SAHotTime([new SAHotTimeBonus((SpecialAttribute) 59, 500), new SAHotTimeBonus((SpecialAttribute) 61, 100)]));
        test.AddEvent(7003, GameEventType.SAHotTime, new SAHotTime([new SAHotTimeBonus((SpecialAttribute) 59, 9000)]),
            DateTime.Now.AddDays(-9), DateTime.Now.AddDays(-2));
        var session = test.Start();

        session.Stats.AddHotTime(session);

        Assert.That(session.Stats.Values[(SpecialAttribute) 59].Total, Is.EqualTo(1500));
        Assert.That(session.Stats.Values[(SpecialAttribute) 61].Total, Is.EqualTo(100));
        Assert.That(session.Stats.Values[(SpecialAttribute) 60].Total, Is.EqualTo(0));
    }
    #endregion

    [Test]
    public void IngredientFeeReduceLimit_IsTheClientsHalf_WhenTheConstantsLackIt() {
        Assert.That(new ConstantsTable().IngredientFeeReduceLimit, Is.EqualTo(0.5f));
    }

    [TestCase(1000, 3000, 700)]
    [TestCase(999, 3000, 700)] // 299 off, whole numbers
    [TestCase(5, 5000, 3)]
    [TestCase(1000, 10000, 0)]
    [TestCase(1000, 20000, 0)]
    [TestCase(1000, 0, 1000)]
    [TestCase(1000, -5, 1000)]
    [TestCase(0, 5000, 0)]
    public void BeautyShopPrice_TakesTheRateOff(int price, int rate, int expected) {
        Assert.That(EventSaleRules.BeautyShopPrice(price, rate), Is.EqualTo(expected));
    }

    // PetComposeSale, SaleGemStoneUpgrade and SaleEnchant: the name, the id and the rate (GameEventSale::vfunc_4).
    [TestCase(GameEventType.PetComposeSale, "PetComposeSale")]
    [TestCase(GameEventType.SaleGemStoneUpgrade, "SaleGemStoneUpgrade")]
    [TestCase(GameEventType.SaleEnchant, "SaleEnchant")] // GameEventSaleEnchant reads one int (gameevent.cpp:1152)
    public void EventRate_WritesTheRate(GameEventType type, string name) {
        Assert.That(Written(type, new EventRate(3000)), Is.EqualTo(Name(name) + "591B0000" + "B80B0000"));
    }

    [Test]
    public void EventRate_RowsAndTheHighestRate() {
        Assert.Multiple(() => {
            Assert.That(EventSaleReader.Rate(" 5000 "), Is.EqualTo(new EventRate(5000)));
            Assert.That(EventSaleReader.Rate("0"), Is.Null);
            Assert.That(EventSaleReader.Rate(null), Is.Null);
            Assert.That(EventSaleReader.Rate("x"), Is.Null);

            var test = new TestGameSession();
            GameEvent low = test.AddEvent(1, GameEventType.PetComposeSale, new EventRate(1000));
            GameEvent high = test.AddEvent(2, GameEventType.PetComposeSale, new EventRate(3000));
            Assert.That(EventSaleRules.HighestRate([low, high]), Is.EqualTo(3000));
            Assert.That(EventSaleRules.HighestRate([]), Is.EqualTo(0));
        });
    }

    // BlackMarketFeeDiscount (GameEventBlackMarketFeeDiscount::vfunc_4): the name, the id, the rate, then the start and
    // end time as two longs (the fee notice shows them).
    [Test]
    public void BlackMarketFeeDiscount_WritesTheRateAndTimes() {
        var test = new TestGameSession();
        GameEvent gameEvent = test.AddEvent(EventId, GameEventType.BlackMarketFeeDiscount, new BlackMarketFeeDiscount(150));
        var writer = new ByteWriter();
        gameEvent.WriteTo(writer);
        var expected = new ByteWriter();
        expected.WriteUnicodeString("BlackMarketFeeDiscount");
        expected.WriteInt(EventId);
        expected.WriteInt(150);
        expected.WriteLong(gameEvent.StartTime);
        expected.WriteLong(gameEvent.EndTime);
        Assert.That(Convert.ToHexString(writer.ToArray()), Is.EqualTo(Convert.ToHexString(expected.ToArray())));
    }

    // The fee rows: 0..10000 (0 is a free sale), anything else is no event; the lowest running rate, -1 without one.
    [Test]
    public void BlackMarketFeeDiscount_RowsAndTheRate() {
        Assert.Multiple(() => {
            Assert.That(EventSaleReader.FeeRate("150"), Is.EqualTo(new BlackMarketFeeDiscount(150)));
            Assert.That(EventSaleReader.FeeRate("0"), Is.EqualTo(new BlackMarketFeeDiscount(0)));
            Assert.That(EventSaleReader.FeeRate("10000"), Is.EqualTo(new BlackMarketFeeDiscount(10000)));
            Assert.That(EventSaleReader.FeeRate("10001"), Is.Null);
            Assert.That(EventSaleReader.FeeRate("-1"), Is.Null);
            Assert.That(EventSaleReader.FeeRate(null), Is.Null);

            var test = new TestGameSession();
            GameEvent a = test.AddEvent(1, GameEventType.BlackMarketFeeDiscount, new BlackMarketFeeDiscount(300));
            GameEvent b = test.AddEvent(2, GameEventType.BlackMarketFeeDiscount, new BlackMarketFeeDiscount(150));
            GameEvent bad = test.AddEvent(3, GameEventType.BlackMarketFeeDiscount, new BlackMarketFeeDiscount(-5));
            Assert.That(EventSaleRules.BlackMarketFeeRate([a, b, bad]), Is.EqualTo(150));
            Assert.That(EventSaleRules.BlackMarketFeeRate([bad]), Is.EqualTo(-1));
            Assert.That(EventSaleRules.BlackMarketFeeRate([]), Is.EqualTo(-1));
        });
    }

    // TransferItemBound (GameEventTransferItemBound::vfunc_4): the name, the id and the flag as one bool.
    [Test]
    public void TransferItemBound_WritesTheFlag() {
        var test = new TestGameSession();
        GameEvent gameEvent = test.AddEvent(EventId, GameEventType.TransferItemBound, new TransferItemBound(true));
        var writer = new ByteWriter();
        gameEvent.WriteTo(writer);
        var expected = new ByteWriter();
        expected.WriteUnicodeString("TransferItemBound");
        expected.WriteInt(EventId);
        expected.WriteBool(true);
        Assert.That(Convert.ToHexString(writer.ToArray()), Is.EqualTo(Convert.ToHexString(expected.ToArray())));
    }

    // The flag rows: no value (all 20 KR rows) is on, as the client's default; otherwise the client's ParseBoolW, on
    // when the text holds 1, o, O, t or T.
    [Test]
    public void TransferItemBound_RowsReadTheFlag() {
        Assert.Multiple(() => {
            Assert.That(EventSaleReader.TransferItemBound(null), Is.EqualTo(new TransferItemBound(true)));
            Assert.That(EventSaleReader.TransferItemBound(""), Is.EqualTo(new TransferItemBound(true)));
            Assert.That(EventSaleReader.TransferItemBound("1"), Is.EqualTo(new TransferItemBound(true)));
            Assert.That(EventSaleReader.TransferItemBound("true"), Is.EqualTo(new TransferItemBound(true)));
            Assert.That(EventSaleReader.TransferItemBound("0"), Is.EqualTo(new TransferItemBound(false)));
            Assert.That(EventSaleReader.TransferItemBound("false"), Is.EqualTo(new TransferItemBound(false)));
        });
    }
}
