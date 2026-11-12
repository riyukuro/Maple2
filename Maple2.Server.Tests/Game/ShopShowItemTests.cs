using System;
using System.Linq;
using Maple2.File.Ingest.Helpers;
using Maple2.Model.Enum;
using Maple2.Model.Game.Shop;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game;

// The NpcShopShowItem game event: a shop a running row names shows only the items its running rows name.
public class ShopShowItemTests {
    private static readonly DateTime Now = new(2026, 11, 12, 12, 0, 0);

    private static GameEventMetadata Row(int id, GameEventData data, DateTime start, DateTime end) =>
        new(id, GameEventType.NpcShopShowItem, start, end, TimeSpan.Zero, TimeSpan.Zero, [], data, "", "", "", "");

    private static ShopItemMetadata Item(int id, int itemId) {
        var requirements = new ShopItemMetadata.Requirement(0, new ShopItemMetadata.Achievement(0, 0), new ShopItemMetadata.Championship(0, 0),
            new ShopItemMetadata.GuildNpc(default, 0), new ShopItemMetadata.QuestAlliance(default, 0));
        return new ShopItemMetadata(id, 254, itemId, 1, new ShopCost { Type = ShopCurrencyType.Meso, Amount = 3000 }, 0, "", requirements,
            null, 1, default, "", false, false, 10000, false);
    }

    // The KR row 13000001: shop 254, its two meso items.
    [Test]
    public void Reader_KrRow() {
        NpcShopShowItem? row = EventSaleReader.ShopShowItem("254", "20000031,20000032");
        Assert.Multiple(() => {
            Assert.That(row?.ShopId, Is.EqualTo(254));
            Assert.That(row?.ItemIds, Is.EqualTo(new[] { 20000031, 20000032 }));
        });
    }

    [Test]
    public void Reader_Spaces() {
        Assert.That(EventSaleReader.ShopShowItem(" 254 ", "20000031, 20000032 ")?.ItemIds, Is.EqualTo(new[] { 20000031, 20000032 }));
    }

    // A row that would empty the shop or names no shop is not stored.
    [TestCase(null, "20000031")]
    [TestCase("", "20000031")]
    [TestCase("0", "20000031")]
    [TestCase("x", "20000031")]
    [TestCase("254", "")]
    [TestCase("254", null)]
    [TestCase("254", "20000031,x")]
    [TestCase("254", "20000031,0")]
    public void Reader_Unusable(string? value1, string? value2) {
        Assert.That(EventSaleReader.ShopShowItem(value1, value2), Is.Null);
    }

    // The client has no class for it: the event list leaves it out.
    [Test]
    public void NotSentToClient() {
        var gameEvent = new GameEvent(Row(1, new NpcShopShowItem(254, [20000031]), DateTime.MinValue, DateTime.MaxValue));
        Assert.That(gameEvent.SentToClient, Is.False);
    }

    // The running rows of the shop together; ended and later rows, other shops and other types do not count.
    [Test]
    public void Shown_RunningRowsOfTheShop() {
        GameEventMetadata[] events = [
            Row(13000001, new NpcShopShowItem(254, [20000031, 20000032]), new DateTime(2019, 1, 17), new DateTime(2099, 12, 31)),
            Row(13000003, new NpcShopShowItem(254, [20000302]), new DateTime(2026, 11, 1), new DateTime(2026, 12, 1)),
            Row(13000002, new NpcShopShowItem(254, [40400112]), new DateTime(2019, 3, 14), new DateTime(2019, 4, 11)),
            Row(13000004, new NpcShopShowItem(254, [50600257]), new DateTime(2027, 1, 1), new DateTime(2099, 1, 1)),
            Row(13000005, new NpcShopShowItem(282, [11304817]), new DateTime(2019, 1, 17), new DateTime(2099, 12, 31)),
            Row(1, new SpawnNpcEvent(2000064, 10, 11001003, 254), DateTime.MinValue, DateTime.MaxValue),
        ];
        Assert.Multiple(() => {
            Assert.That(ShopShowItemRules.Shown(events, 254, Now), Is.EquivalentTo(new[] { 20000031, 20000032, 20000302 }));
            Assert.That(ShopShowItemRules.Shown(events, 282, Now), Is.EquivalentTo(new[] { 11304817 }));
            Assert.That(ShopShowItemRules.Shown(events, 316, Now), Is.Null);
            Assert.That(ShopShowItemRules.Shown(events, 254, new DateTime(2018, 1, 1)), Is.Null);
        });
    }

    // Shop 254 holds the 16 coin items of the 2019 rows and the two meso items: only the meso items stay.
    [Test]
    public void Filter_KeepsTheShownItems() {
        ShopItemMetadata[] items = [Item(1, 40400112), Item(2, 50600257), Item(17, 20000031), Item(18, 20000032)];
        Assert.Multiple(() => {
            Assert.That(ShopShowItemRules.Filter(items, new System.Collections.Generic.HashSet<int> { 20000031, 20000032 }).Select(item => item.Id),
                Is.EqualTo(new[] { 17, 18 }));
            Assert.That(ShopShowItemRules.Filter(items, null), Is.EqualTo(items));
        });
    }
}
