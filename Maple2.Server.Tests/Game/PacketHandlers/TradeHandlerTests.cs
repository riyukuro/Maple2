using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Game;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// A trade between two sessions, from the accepted request on (the request itself looks the other player up in the
// field, which the harness does not have). The answers are in the order KMS2 sends them.
public class TradeHandlerTests {
    private TestGameSession giverTest = null!;
    private TestGameSession takerTest = null!;
    private readonly TradeHandler handler = new();
    private Item item = null!;
    private GameSession giver => giverTest.Session;
    private GameSession taker => takerTest.Session;

    [SetUp]
    public void StartTrade() {
        giverTest = new TestGameSession();
        takerTest = new TestGameSession();
        var metadata = TestGameSession.Metadata(20000001, type: 2);
        item = giverTest.Store(metadata, amount: 3);
        takerTest.Register(metadata);
        foreach (TestGameSession test in new[] { giverTest, takerTest }) {
            test.SetServerTable(test.ServerTableMetadata.ConstantsTable with { TradeMaxMeso = 1000000 });
        }
        giverTest.Start();
        takerTest.Start();
        taker.Currency.Meso = 5000;
        var trade = new TradeManager(giver, taker);
        giver.Trade = trade;
        taker.Trade = trade;
        trade.Acknowledge(taker);
        trade.Accept(taker);
        giverTest.TakeSent();
        takerTest.TakeSent();
    }

    private void Send(GameSession session, byte command, Action<Maple2.PacketLib.Tools.ByteWriter>? write = null) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(command);
            write?.Invoke(writer);
        }));
    }

    private void Offer() {
        Send(giver, 8, writer => {
            writer.WriteLong(item.Uid);
            writer.WriteInt(3);
            writer.WriteInt(0);
        });
        Send(taker, 10, writer => writer.WriteLong(1000));
    }

    private static string Hex(Maple2.PacketLib.Tools.ByteWriter packet) => Convert.ToHexString(packet.ToArray());
    private static string[] Modes(List<byte[]> sent) => sent.Select(packet => $"{TestGameSession.Op(packet)} {packet[2]}").ToArray();

    // An offered item leaves the bag, then enters the window; offered mesos leave the player, then enter the window.
    [Test]
    public void Offer_Order() {
        Offer();

        Assert.That(Modes(giverTest.TakeSent()), Is.EqualTo(new[] { "ItemInventory 1", "Trade 8", "Trade 10" }));
        List<byte[]> sent = takerTest.TakeSent();
        Assert.That(sent.Select(TestGameSession.Op), Is.EqualTo(new[] { SendOp.Trade, SendOp.Meso, SendOp.Trade }));
        Assert.That(Convert.ToHexString(sent[2]), Is.EqualTo(Hex(TradePacket.SetMesos(true, 1000))));
        Assert.That(taker.Currency.Meso, Is.EqualTo(4000));
    }

    // The finish: the giver's window loses the item; the taker gets the item as an own offer, its removal, then the
    // bag's add; the mesos come after the items; EndTrade is last. A side that gets no mesos gets no Meso.
    [Test]
    public void Complete_HandsTheOffersOver() {
        Offer();
        Send(giver, 11);
        Send(taker, 11);
        Send(giver, 13);
        giverTest.TakeSent();
        takerTest.TakeSent();

        Send(taker, 13);

        List<byte[]> toGiver = giverTest.TakeSent();
        Assert.That(toGiver.Select(TestGameSession.Op), Is.EqualTo(new[] { SendOp.Trade, SendOp.Trade, SendOp.Meso, SendOp.Trade }));
        Assert.That(new[] { toGiver[0], toGiver[1], toGiver[3] }.Select(Convert.ToHexString), Is.EqualTo(new[] {
            Hex(TradePacket.Complete(false)),
            Hex(TradePacket.RemoveItem(true, 0, item.Uid)),
            Hex(TradePacket.EndTrade(true)),
        }));
        List<byte[]> toTaker = takerTest.TakeSent();
        Assert.That(Modes(toTaker).Take(4), Is.EqualTo(new[] { "Trade 13", "Trade 8", "Trade 9", "ItemInventory 0" }));
        Assert.That(Convert.ToHexString(toTaker[2]), Is.EqualTo(Hex(TradePacket.RemoveItem(true, 0, item.Uid))));
        Assert.That(toTaker.Select(TestGameSession.Op), Does.Not.Contain(SendOp.Meso));
        Assert.That(Convert.ToHexString(toTaker[^1]), Is.EqualTo(Hex(TradePacket.EndTrade(true))));

        Assert.That(giver.Currency.Meso, Is.EqualTo(1000));
        Assert.That(taker.Currency.Meso, Is.EqualTo(4000));
        Assert.That(giver.Item.Inventory.Find(20000001), Is.Empty);
        Assert.That(taker.Item.Inventory.Find(20000001).Sum(got => got.Amount), Is.EqualTo(3));
        Assert.That(giver.Trade, Is.Null);
        Assert.That(taker.Trade, Is.Null);
    }

    // A cancel: each own item leaves the window and enters the bag, the mesos come back, then EndTrade (failed).
    [Test]
    public void Cancel_ReturnsTheOffers() {
        Offer();
        giverTest.TakeSent();
        takerTest.TakeSent();

        Send(giver, 7);

        List<byte[]> toGiver = giverTest.TakeSent();
        Assert.That(Modes(toGiver), Is.EqualTo(new[] { "Trade 9", "ItemInventory 0", "Trade 6" }));
        Assert.That(Convert.ToHexString(toGiver[2]), Is.EqualTo(Hex(TradePacket.EndTrade(false))));
        List<byte[]> toTaker = takerTest.TakeSent();
        Assert.That(toTaker.Select(TestGameSession.Op), Is.EqualTo(new[] { SendOp.Meso, SendOp.Trade }));
        Assert.That(taker.Currency.Meso, Is.EqualTo(5000));
        Assert.That(giver.Item.Inventory.Find(20000001).Sum(got => got.Amount), Is.EqualTo(3));
    }
}
