using System;
using System.Linq;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using static Maple2.Model.Error.TradeError;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The asked player's client answers a trade request (CTradeManagerEx::OnTradeRequest, trademanagerex_1.cpp:384-420)
// with the requester's character id: Trade 2 once its request box is up (SendTradeMode2, :318), or Trade 14 when
// another message box is already open and the request cannot be shown (SendTradeMode14, :326).
public class TradeRequestAnswerTests {
    private TestGameSession giverTest = null!;
    private TestGameSession takerTest = null!;
    private readonly TradeHandler handler = new();
    private GameSession giver => giverTest.Session;
    private GameSession taker => takerTest.Session;
    private TradeManager trade = null!;

    [SetUp]
    public void SetUp() {
        giverTest = new TestGameSession();
        takerTest = new TestGameSession(giverTest.Options);
        foreach (TestGameSession test in new[] { giverTest, takerTest }) {
            test.SetServerTable(test.ServerTableMetadata.ConstantsTable with { TradeRequestDuration = 30 });
            test.Start();
            test.EnterMap(TestGameSession.Map(2000062));
        }
        // The requester stands in the asked player's field.
        taker.Field!.Players[2] = giver.Player;
        trade = new TradeManager(giver, taker);
        giver.Trade = trade;
        giverTest.TakeSent();
        takerTest.TakeSent();
    }

    [TearDown]
    public void TearDown() => trade.Dispose();

    private string[] Answer(byte mode, long characterId) {
        handler.Handle(taker, TestGameSession.Packet(writer => {
            writer.WriteByte(mode);
            writer.WriteLong(characterId);
        }));
        return takerTest.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.Trade).Select(Convert.ToHexString).ToArray();
    }

    private string[] GiverHeard() => giverTest.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.Trade).Select(Convert.ToHexString).ToArray();

    private static string Hex(Maple2.PacketLib.Tools.ByteWriter packet) => Convert.ToHexString(packet.ToArray());

    // 2: the asked player holds the request, and the requester is told the box is up.
    [Test]
    public void Acknowledge_GivesTheAskedPlayerTheRequest() {
        Assert.That(Answer(2, giver.CharacterId), Is.Empty);
        Assert.Multiple(() => {
            Assert.That(taker.Trade, Is.SameAs(trade));
            Assert.That(GiverHeard(), Is.EqualTo(new[] { Hex(TradePacket.Acknowledge()) }));
            Assert.That(trade.Accept(taker), Is.True, "the box's accept then starts the trade");
        });
    }

    // 2 naming no player of the field, or with a trade already held, is refused and holds nothing.
    [Test]
    public void Acknowledge_OfNoRequester_IsDeclined() {
        Assert.That(Answer(2, 12345), Is.EqualTo(new[] { Hex(TradePacket.Error(s_trade_error_decline)) }));
        Assert.Multiple(() => {
            Assert.That(taker.Trade, Is.Null);
            Assert.That(GiverHeard(), Is.Empty);
        });
    }

    [Test]
    public void Acknowledge_WhileTrading_IsTradingNow() {
        Assert.That(Answer(2, giver.CharacterId), Is.Empty);
        GiverHeard();
        Assert.That(Answer(2, giver.CharacterId), Is.EqualTo(new[] { Hex(TradePacket.Error(s_trade_error_trading_now)) }));
        Assert.That(taker.Trade, Is.SameAs(trade));
    }

    // 14: the request ends, and the requester hears it timed out with the asked player's name.
    [Test]
    public void Busy_EndsTheRequest() {
        Assert.That(Answer(14, giver.CharacterId), Is.Empty);
        Assert.Multiple(() => {
            Assert.That(GiverHeard(), Is.EqualTo(new[] { Hex(TradePacket.Error(s_trade_error_timeout, name: taker.PlayerName)) }));
            Assert.That((giver.Trade, taker.Trade), Is.EqualTo(((TradeManager?) null, (TradeManager?) null)));
        });
    }

    // 14 naming another player, or after the request was acknowledged, leaves the request.
    [Test]
    public void Busy_OfNoRequesterOrAfterTheBox_LeavesTheRequest() {
        Assert.That(Answer(14, 12345), Is.Empty);
        Assert.That(giver.Trade, Is.SameAs(trade));
        Assert.That(Answer(2, giver.CharacterId), Is.Empty);
        GiverHeard();
        Assert.That(Answer(14, giver.CharacterId), Is.Empty);
        Assert.Multiple(() => {
            Assert.That(GiverHeard(), Is.Empty);
            Assert.That((giver.Trade, taker.Trade), Is.EqualTo((trade, trade)));
        });
    }
}
