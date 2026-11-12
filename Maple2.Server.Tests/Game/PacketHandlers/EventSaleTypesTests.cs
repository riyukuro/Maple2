using System;
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
}
