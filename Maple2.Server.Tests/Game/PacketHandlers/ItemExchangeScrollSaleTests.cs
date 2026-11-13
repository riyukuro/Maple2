using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.File.Ingest.Helpers;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using BagItem = Maple2.Model.Game.Item;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The exchange scroll fee sale (GameEventExchangeScrollSale, commonfiles/gameevent.cpp:6313): the dialog shows and
// tests the row's fee lowered by the running event's rate (CExchangeItemInfo::GetMeso, itemexchangescrolltable.cpp:57).
// Before, the server read no such event and charged the full fee.
public class ItemExchangeScrollSaleTests {
    private const int ExchangeId = 77;
    private const int ScrollId = 20000001;
    private const int RewardId = 30000002;
    private const int EventId = 7001;

    private TestGameSession test = null!;

    private GameSession Start(long fee, long meso, params (int Id, ExchangeScrollSale Sale)[] sales) {
        test = new TestGameSession();
        foreach ((int id, ExchangeScrollSale sale) in sales) {
            test.AddEvent(id, GameEventType.ExchangeScrollSale, sale);
        }
        test.SetTable(new ItemExchangeScrollTable(new Dictionary<int, ItemExchangeScrollMetadata> {
            [ExchangeId] = new(new ItemComponent(ScrollId, -1, 1, ItemTag.None), new ItemComponent(RewardId, 1, 1, ItemTag.None), 0, fee, []),
        }));
        ItemMetadata scroll = TestGameSession.Metadata(ScrollId, type: 1, slotMax: 100,
            function: new ItemMetadataFunction(ItemFunction.ItemExchangeScroll, "ItemExchangeScroll", $"{ExchangeId}", false));
        BagItem stored = test.Store(scroll);
        test.Register(TestGameSession.Metadata(RewardId, slotMax: 100));
        GameSession session = test.Start();
        test.TakeSent();
        session.Item.Inventory.Get(stored.Uid)!.Amount = 10;
        session.Currency.Meso = meso;
        return session;
    }

    private void Exchange(GameSession session, int count) {
        long scrollUid = session.Item.Inventory.Find(ScrollId).First().Uid;
        new ItemExchangeScroll { ItemMetadata = session.ItemMetadata, TableMetadata = test.TableMetadata }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(1);
            writer.WriteLong(scrollUid);
            writer.WriteLong(0);
            writer.WriteInt(count);
        }));
    }

    private static int Rewards(GameSession session) => session.Item.Inventory.Find(RewardId).Sum(item => item.Amount);

    // A sale of 6000 on the row: one exchange costs (long) (999 * 0.4 + 0.000001) = 399, three cost 1197.
    [Test]
    public void RunningSale_ChargesTheLoweredFee() {
        GameSession session = Start(999, 10000, (EventId, new ExchangeScrollSale(6000, [5, ExchangeId])));
        Exchange(session, 3);
        Assert.Multiple(() => {
            Assert.That(Rewards(session), Is.EqualTo(3));
            Assert.That(session.Currency.Meso, Is.EqualTo(10000 - 1197));
        });
    }

    // The meso the dialog lets through under the sale is enough for the server.
    [Test]
    public void RunningSale_TheLoweredFeeIsEnough() {
        GameSession session = Start(1000, 500, (EventId, new ExchangeScrollSale(5000, [ExchangeId])));
        Exchange(session, 1);
        Assert.Multiple(() => {
            Assert.That(Rewards(session), Is.EqualTo(1));
            Assert.That(session.Currency.Meso, Is.Zero);
        });
    }

    // A sale of another row lowers nothing.
    [Test]
    public void OtherRowSale_FullFee() {
        GameSession session = Start(1000, 5000, (EventId, new ExchangeScrollSale(5000, [ExchangeId + 1])));
        Exchange(session, 2);
        Assert.That(session.Currency.Meso, Is.EqualTo(3000));
    }

    [TestCase(999, 0, 999)]
    [TestCase(999, 6000, 399)]
    [TestCase(1000, 5000, 500)]
    [TestCase(1000, 10000, 0)]
    [TestCase(1000, 12000, 0)]
    [TestCase(1000, -5, 1000)]
    [TestCase(30000000, 6000, 12000000)]
    public void ExchangeScrollFee_TakesTheRateOff(long meso, int rate, long expected) {
        Assert.That(EventSaleRules.ExchangeScrollFee(meso, rate), Is.EqualTo(expected));
    }

    // Of two events of one row, the one that started last counts (the client sets the rate as each starts).
    [Test]
    public void TwoSales_TheLaterStartCounts() {
        var test = new TestGameSession();
        GameEvent early = test.AddEvent(7001, GameEventType.ExchangeScrollSale, new ExchangeScrollSale(8000, [ExchangeId]), DateTime.Now.AddDays(-5));
        GameEvent late = test.AddEvent(7002, GameEventType.ExchangeScrollSale, new ExchangeScrollSale(3000, [ExchangeId]), DateTime.Now.AddDays(-1));
        Assert.Multiple(() => {
            Assert.That(EventSaleRules.ExchangeScrollSaleRate([late, early], ExchangeId), Is.EqualTo(3000));
            Assert.That(EventSaleRules.ExchangeScrollSaleRate([late, early], ExchangeId + 1), Is.Zero);
        });
    }

    // The event list entry: the base, then the rate, the count and the row ids (vfunc_4, gameevent.cpp:6349).
    [Test]
    public void Written_RateCountIds() {
        var test = new TestGameSession();
        GameEvent gameEvent = test.AddEvent(EventId, GameEventType.ExchangeScrollSale, new ExchangeScrollSale(6000, [36000172, 36000347]));
        var writer = new ByteWriter();
        gameEvent.WriteTo(writer);
        var name = new ByteWriter();
        name.WriteUnicodeString("ExchangeScrollSale");
        Assert.That(Convert.ToHexString(writer.ToArray()),
            Is.EqualTo(Convert.ToHexString(name.ToArray()) + "591B0000" + "70170000" + "02000000" + "AC512502" + "5B522502"));
    }

    [TestCase("36000172,36000347", "6000", 6000, new[] { 36000172, 36000347 })]
    [TestCase(" 36000448 ", "6000", 6000, new[] { 36000448 })]
    public void Reader_IdsAndRate(string value1, string value2, int rate, int[] ids) {
        ExchangeScrollSale? sale = EventSaleReader.ExchangeScroll(value1, value2);
        Assert.That(sale, Is.Not.Null);
        Assert.Multiple(() => {
            Assert.That(sale!.Rate, Is.EqualTo(rate));
            Assert.That(sale.ExchangeIds, Is.EqualTo(ids));
        });
    }

    [TestCase("36000172", "")]
    [TestCase("36000172", "0")]
    [TestCase("", "6000")]
    [TestCase("36000172,x", "6000")]
    public void Reader_NoEvent(string value1, string value2) {
        Assert.That(EventSaleReader.ExchangeScroll(value1, value2), Is.Null);
    }
}
