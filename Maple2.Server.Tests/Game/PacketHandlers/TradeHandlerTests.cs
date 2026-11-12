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

    // A refused offer: the notice (or nothing), and neither the bag nor the mesos nor the other window change.
    // 6000 is more than the player has, 2000000 more than a trade takes, -5 is not an amount; after the player's
    // own lock every change is refused as latched.
    [TestCase(6000L, false, "s_trade_error_meso")]
    [TestCase(2000000L, false, "s_trade_error_invalid_meso")]
    [TestCase(-5L, false, null)]
    [TestCase(1000L, true, "s_trade_error_latched")]
    public void SetMesos_Refused(long amount, bool locked, string? error) {
        if (locked) {
            Send(taker, 11);
            giverTest.TakeSent();
            takerTest.TakeSent();
        }

        Send(taker, 10, writer => writer.WriteLong(amount));

        string[] expected = error == null ? [] : [Hex(TradePacket.Error(Enum.Parse<Maple2.Model.Error.TradeError>(error)))];
        Assert.That(takerTest.TakeSent().Select(Convert.ToHexString), Is.EqualTo(expected));
        Assert.That(giverTest.TakeSent(), Is.Empty);
        Assert.That(taker.Currency.Meso, Is.EqualTo(5000));
        Send(taker, 7);
        Assert.That(taker.Currency.Meso, Is.EqualTo(5000));
    }

    // A refused item: more than the stack, no amount, an item the player does not have, or any item after the
    // player's own lock. The stack stays whole in the bag and the other player is shown nothing.
    [TestCase(4, false, false)]
    [TestCase(0, false, false)]
    [TestCase(-1, false, false)]
    [TestCase(3, true, false)]
    [TestCase(3, false, true)]
    public void AddItem_Refused(int amount, bool unknown, bool locked) {
        if (locked) {
            Send(giver, 11);
            giverTest.TakeSent();
            takerTest.TakeSent();
        }

        Send(giver, 8, writer => {
            writer.WriteLong(unknown ? item.Uid + 50 : item.Uid);
            writer.WriteInt(amount);
            writer.WriteInt(0);
        });

        // More than the stack is answered by the bag (the count is wrong); the lock by the trade.
        string[] expected = locked ? [Hex(TradePacket.Error(Maple2.Model.Error.TradeError.s_trade_error_latched))]
            : amount == 4 ? [Hex(ItemInventoryPacket.Error(Maple2.Model.Error.ItemInventoryError.s_item_err_invalid_count))] : [];
        Assert.That(giverTest.TakeSent().Select(Convert.ToHexString), Is.EqualTo(expected));
        Assert.That(takerTest.TakeSent(), Is.Empty);
        Assert.That(giver.Item.Inventory.Get(item.Uid)?.Amount, Is.EqualTo(3));
        Send(giver, 7);
        Assert.That(giver.Item.Inventory.Find(20000001).Sum(got => got.Amount), Is.EqualTo(3));
    }

    // KMS2 order: a player who has locked is unlocked (mode 12) before the other player's change is shown.
    [Test]
    public void Offer_UnlocksTheOtherSideFirst() {
        Send(taker, 11);
        giverTest.TakeSent();
        takerTest.TakeSent();

        Send(giver, 8, writer => {
            writer.WriteLong(item.Uid);
            writer.WriteInt(3);
            writer.WriteInt(0);
        });

        Assert.That(Modes(takerTest.TakeSent()), Is.EqualTo(new[] { "Trade 12", "Trade 8" }));
        Assert.That(Modes(giverTest.TakeSent()), Is.EqualTo(new[] { "ItemInventory 1", "Trade 12", "Trade 8" }));

        Send(giver, 11);
        giverTest.TakeSent();
        takerTest.TakeSent();
        Send(taker, 10, writer => writer.WriteLong(1000));

        List<byte[]> toGiver = giverTest.TakeSent();
        Assert.That(Modes(toGiver), Is.EqualTo(new[] { "Trade 12", "Trade 10" }));
        Assert.That(Convert.ToHexString(toGiver[0]), Is.EqualTo(Hex(TradePacket.UnFinalize(true))));
    }

    // A cancel: the own mesos come back before the own items.
    [Test]
    public void Cancel_ReturnsTheMesosFirst() {
        giver.Currency.Meso = 700;
        Send(giver, 8, writer => {
            writer.WriteLong(item.Uid);
            writer.WriteInt(3);
            writer.WriteInt(0);
        });
        Send(giver, 10, writer => writer.WriteLong(700));
        giverTest.TakeSent();

        Send(taker, 7);

        Assert.That(Modes(giverTest.TakeSent()), Is.EqualTo(new[] { "Meso 0", "Trade 9", "ItemInventory 0", "Trade 6" }).Using<string, string>((got, want) => got.Split(' ')[0] == "Meso" ? want.StartsWith("Meso") : got == want));
        Assert.That(giver.Currency.Meso, Is.EqualTo(700));
    }

    // An offer racing the other player's cancel: whichever comes first, the item is in the giver's bag and the
    // mesos with the taker afterwards, and nothing is left in a window of an ended trade.
    [Test]
    public void Offer_RacingACancelLosesNothing() {
        for (int round = 0; round < 60; round++) {
            StartTrade();
            giver.Currency.Meso = 800;

            System.Threading.Tasks.Parallel.Invoke(
                () => {
                    Send(giver, 8, writer => {
                        writer.WriteLong(item.Uid);
                        writer.WriteInt(2);
                        writer.WriteInt(0);
                    });
                    Send(giver, 10, writer => writer.WriteLong(800));
                },
                () => Send(taker, round % 2 == 0 ? (byte) 10 : (byte) 7, writer => writer.WriteLong(1000)),
                () => Send(taker, 7));

            Assert.That(giver.Item.Inventory.Find(20000001).Sum(got => got.Amount), Is.EqualTo(3), $"round {round}");
            Assert.That(giver.Currency.Meso, Is.EqualTo(800), $"round {round}");
            Assert.That(taker.Currency.Meso, Is.EqualTo(5000), $"round {round}");
            Assert.That(giver.Trade, Is.Null, $"round {round}");
            Assert.That(giverTest.Errors.Concat(takerTest.Errors), Is.Empty, $"round {round}");
        }
    }
}
