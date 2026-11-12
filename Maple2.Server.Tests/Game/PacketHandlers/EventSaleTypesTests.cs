using System;
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
}
