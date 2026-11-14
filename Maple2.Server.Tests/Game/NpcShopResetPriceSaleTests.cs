using System;
using System.Collections.Generic;
using Maple2.File.Ingest.Helpers;
using Maple2.Model.Enum;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game;

// GameEventNpcShopResetPriceSale: value1 the shop, value2 the rate out of 10000 taken off the shop's instant restock
// price. The client keeps one rate per shop, the last event of the list wins, and its shop window shows and sends
// back the lowered price (UIShopDialog::UpdateMoney). The restock charge is in ShopHandlerTests. The event ids are
// made up.
public class NpcShopResetPriceSaleTests {
    [TestCase("109", "2500", 109, 2500)]
    [TestCase(" 7 ", " 10000 ", 7, 10000)]
    public void Reader_ShopAndRate(string value1, string value2, int shop, int rate) {
        Assert.That(EventSaleReader.NpcShopReset(value1, value2), Is.EqualTo(new NpcShopResetPriceSale(shop, rate)));
    }

    [TestCase("0", "2500")]
    [TestCase("", "2500")]
    [TestCase("x", "2500")]
    [TestCase("109", "0")]
    [TestCase("109", "-5")]
    [TestCase("109", "10001")]
    [TestCase("109", "")]
    [TestCase(null, null)]
    public void Reader_NoShopOrRateGivesNoEvent(string? value1, string? value2) {
        Assert.That(EventSaleReader.NpcShopReset(value1, value2), Is.Null);
    }

    // (price * (1 - rate / 10000) + 0.000001) in doubles, cut; nothing taken off without a rate.
    [TestCase(300, 2500, 225)]
    [TestCase(10, 1000, 9)]
    [TestCase(50000, 3333, 33335)]
    [TestCase(150, 10000, 0)]
    [TestCase(150, 20000, 0)]
    [TestCase(7, 5000, 3)]
    [TestCase(300, 0, 300)]
    [TestCase(0, 2500, 0)]
    public void Price_IsTheClientRule(int price, int rate, int expected) {
        Assert.That(EventSaleRules.NpcShopResetPrice(price, rate), Is.EqualTo(expected));
    }

    [Test]
    public void GameEvent_IsLaidOutForTheClient() {
        var gameEvent = new GameEvent(new GameEventMetadata(990001, GameEventType.NpcShopResetPriceSale, DateTime.UnixEpoch,
            DateTime.UnixEpoch.AddSeconds(1000), TimeSpan.Zero, TimeSpan.Zero, [], new NpcShopResetPriceSale(109, 2500), "109", "2500", "", ""));
        Assert.That(gameEvent.SentToClient, Is.True);
        var writer = new ByteWriter();
        gameEvent.WriteTo(writer);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("NpcShopResetPriceSale"));
        Assert.That(reader.ReadInt(), Is.EqualTo(990001)); // GameEvent::vfunc_4
        Assert.That(reader.ReadInt(), Is.EqualTo(109)); // the shop (m_d0)
        Assert.That(reader.ReadInt(), Is.EqualTo(2500)); // the rate (m_d4)
        Assert.That(reader.Position, Is.EqualTo(writer.Length));
    }

    // The shop's last running event by start, then id; other shops and ended events are left out.
    [Test]
    public void Rate_LastRunningEventOfTheShop() {
        var test = new TestGameSession();
        DateTime end = DateTime.Now.AddDays(2);
        DateTime earlier = DateTime.Now.AddDays(-3);
        DateTime later = DateTime.Now.AddDays(-1);
        test.AddEvent(990001, GameEventType.NpcShopResetPriceSale, new NpcShopResetPriceSale(109, 5000), earlier, end);
        test.AddEvent(990002, GameEventType.NpcShopResetPriceSale, new NpcShopResetPriceSale(109, 2000), later, end);
        test.AddEvent(990003, GameEventType.NpcShopResetPriceSale, new NpcShopResetPriceSale(110, 3000), later, end);
        test.AddEvent(990004, GameEventType.NpcShopResetPriceSale, new NpcShopResetPriceSale(110, 1000), later, end);
        test.AddEvent(990005, GameEventType.NpcShopResetPriceSale, new NpcShopResetPriceSale(111, 9000), DateTime.Now.AddDays(-9), DateTime.Now.AddDays(-2));
        GameSession session = test.Start();
        IList<GameEvent> events = session.FindEvent(GameEventType.NpcShopResetPriceSale);

        Assert.That(EventSaleRules.NpcShopResetRate(events, 109), Is.EqualTo(2000));
        Assert.That(EventSaleRules.NpcShopResetRate(events, 110), Is.EqualTo(1000));
        Assert.That(EventSaleRules.NpcShopResetRate(events, 111), Is.EqualTo(0));
        Assert.That(EventSaleRules.NpcShopResetRate(events, 112), Is.EqualTo(0));
    }
}
