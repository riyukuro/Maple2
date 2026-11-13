using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using static Maple2.Model.Error.TradeError;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// Trade 0 (request) from a player who already has a trade: while the player's own request still waits, KMS2 answers
// with mode 14 (s_trade_already_requested, no body); any other trade gets s_trade_error_trading_now.
public class TradeRequestHandlerTests {
    private TestGameSession giverTest = null!;
    private TestGameSession takerTest = null!;
    private readonly TradeHandler handler = new();
    private GameSession giver => giverTest.Session;
    private GameSession taker => takerTest.Session;
    private TradeManager trade = null!;

    [SetUp]
    public void SetUp() {
        giverTest = new TestGameSession();
        takerTest = new TestGameSession();
        // The request's time-out is set far off, so it does not end the request mid-test.
        foreach (TestGameSession test in new[] { giverTest, takerTest }) {
            test.SetServerTable(test.ServerTableMetadata.ConstantsTable with { TradeRequestDuration = 30 });
            test.Start();
            test.EnterMap(TestGameSession.Map(2000062));
        }
        trade = new TradeManager(giver, taker);
        giver.Trade = trade;
        giverTest.TakeSent();
        takerTest.TakeSent();
    }

    [TearDown]
    public void TearDown() => trade.Dispose();

    private List<string> Request(TestGameSession from, long characterId) {
        handler.Handle(from.Session, TestGameSession.Packet(writer => {
            writer.WriteByte(0);
            writer.WriteLong(characterId);
        }));
        return from.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.Trade).Select(Convert.ToHexString).ToList();
    }

    private static string Hex(Maple2.PacketLib.Tools.ByteWriter packet) => Convert.ToHexString(packet.ToArray());

    [Test]
    public void ARepeatRequestWhileMineWaits_IsAlreadyRequested() {
        byte[] expected = { (byte) SendOp.Trade, (byte) ((int) SendOp.Trade >> 8), 14 };
        Assert.That(Hex(TradePacket.AlreadyRequest()), Is.EqualTo(Convert.ToHexString(expected)));

        Assert.That(Request(giverTest, taker.CharacterId), Is.EqualTo(new[] { Convert.ToHexString(expected) }));
        // The waiting request stays as it was, and the other player hears nothing.
        Assert.That(giver.Trade, Is.SameAs(trade));
        Assert.That(takerTest.TakeSent(), Is.Empty);
    }

    [Test]
    public void ARepeatRequestAfterTheAnswerWaits_IsAlreadyRequested() {
        Assert.That(trade.Acknowledge(taker), Is.True);
        giverTest.TakeSent();

        Assert.That(Request(giverTest, 12345), Is.EqualTo(new[] { Hex(TradePacket.AlreadyRequest()) }));
        Assert.That(giver.Trade, Is.SameAs(trade));
    }

    // The one asked, holding the request it has answered, is trading now.
    [Test]
    public void ARequestFromTheOneAsked_IsTradingNow() {
        Assert.That(trade.Acknowledge(taker), Is.True);
        takerTest.TakeSent();

        Assert.That(Request(takerTest, giver.CharacterId), Is.EqualTo(new[] { Hex(TradePacket.Error(s_trade_error_trading_now)) }));
        Assert.That(taker.Trade, Is.SameAs(trade));
    }

    [Test]
    public void ARequestMidTrade_IsTradingNow() {
        Assert.That(trade.Acknowledge(taker), Is.True);
        trade.Accept(taker);
        giverTest.TakeSent();

        Assert.That(Request(giverTest, taker.CharacterId), Is.EqualTo(new[] { Hex(TradePacket.Error(s_trade_error_trading_now)) }));
        Assert.That(giver.Trade, Is.SameAs(trade));
    }
}
