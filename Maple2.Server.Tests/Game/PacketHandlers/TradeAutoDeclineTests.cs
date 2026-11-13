using System;
using System.Linq;
using System.Reflection;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// Trade 4 sent by the client itself when it refuses a request (the trade refusal option or the GM action block 0x20,
// CTradeManagerEx::OnTradeRequest trademanagerex_1.cpp:390/397, FUN_14185b8e0 :252): u8 4, i64 the requester's
// character id, with no Acknowledge (Trade 2) before it. The requester gets the decline line (Trade 4, the name).
public class TradeAutoDeclineTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private TestGameSession giverTest = null!;
    private TestGameSession takerTest = null!;
    private TradeManager trade = null!;
    private readonly TradeHandler handler = new();

    [SetUp]
    public void SetUp() {
        giverTest = new TestGameSession();
        giverTest.SetServerTable(giverTest.ServerTableMetadata.ConstantsTable with { TradeRequestDuration = 30 });
        giverTest.Start();
        takerTest = new TestGameSession(giverTest.Options);
        takerTest.SetServerTable(takerTest.ServerTableMetadata.ConstantsTable with { TradeRequestDuration = 30 });
        takerTest.Start();
        // Both players in one field, as the request needs.
        FieldManager field = giverTest.Session.Field!;
        typeof(Actor<Player>).GetField("<ObjectId>k__BackingField", Any)!.SetValue(takerTest.Session.Player, 2);
        field.Players[2] = takerTest.Session.Player;
        takerTest.Session.Field = field;

        trade = new TradeManager(giverTest.Session, takerTest.Session);
        giverTest.Session.Trade = trade;
        giverTest.TakeSent();
        takerTest.TakeSent();
    }

    [TearDown]
    public void TearDown() => trade.Dispose();

    private static ByteReader Decline(long requester) {
        var writer = new ByteWriter();
        writer.WriteByte(4); // FUN_14185b8e0: bAccept 0
        writer.WriteLong(requester);
        return new ByteReader(writer.ToArray(), 0);
    }

    [Test]
    public void ARefusalWithoutAcknowledge_EndsTheRequest() {
        ByteReader packet = Decline(giverTest.Session.CharacterId);
        handler.Handle(takerTest.Session, packet);

        Assert.Multiple(() => {
            Assert.That(packet.Available, Is.EqualTo(0));
            Assert.That(giverTest.Session.Trade, Is.Null);
            Assert.That(giverTest.TakeSent().Where(sent => TestGameSession.Op(sent) == SendOp.Trade).Select(Convert.ToHexString),
                Is.EqualTo(new[] { Convert.ToHexString(TradePacket.Decline(takerTest.Session.PlayerName).ToArray()) }));
        });
    }

    // The request box's decline, after Acknowledge, still ends it the same way.
    [Test]
    public void ADeclineAfterAcknowledge_EndsTheRequest() {
        Assert.That(trade.Acknowledge(takerTest.Session), Is.True);
        giverTest.TakeSent();

        handler.Handle(takerTest.Session, Decline(giverTest.Session.CharacterId));
        Assert.Multiple(() => {
            Assert.That(giverTest.Session.Trade, Is.Null);
            Assert.That(takerTest.Session.Trade, Is.Null);
            Assert.That(giverTest.TakeSent().Count(sent => TestGameSession.Op(sent) == SendOp.Trade), Is.EqualTo(1));
        });
    }

    // Only the one asked may decline: the requester naming itself does not end its own request.
    [Test]
    public void TheRequesterCannotDeclineItsOwnRequest() {
        handler.Handle(giverTest.Session, Decline(giverTest.Session.CharacterId));
        Assert.That(giverTest.Session.Trade, Is.SameAs(trade));
    }
}
