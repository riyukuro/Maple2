using System;
using System.Linq;
using System.Reflection;
using Maple2.Model.Game;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using static Maple2.Model.Error.TradeError;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// A trade's start as the two clients send it: Trade 0 (the request, the other's character id) gives the requester the
// trade; Trade 2 (the request box shown) gives it to the one asked; Trade 14 (the box could not be shown) ends it with
// the time-out line. A request to a player not in the field, or one already asked, is refused.
public class TradeRequestFlowTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private TestGameSession giverTest = null!;
    private TestGameSession takerTest = null!;
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
        giverTest.TakeSent();
        takerTest.TakeSent();
    }

    [TearDown]
    public void TearDown() {
        giverTest.Session.Trade?.Dispose();
        takerTest.Session.Trade?.Dispose();
    }

    private void Send(TestGameSession from, byte command, long characterId) {
        handler.Handle(from.Session, TestGameSession.Packet(writer => {
            writer.WriteByte(command);
            writer.WriteLong(characterId);
        }));
    }

    private static string[] Trade(TestGameSession test) =>
        test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.Trade).Select(Convert.ToHexString).ToArray();

    private static string Hex(Maple2.PacketLib.Tools.ByteWriter packet) => Convert.ToHexString(packet.ToArray());

    [Test]
    public void ARequestAndItsBox_GiveBothPlayersTheTrade() {
        Send(giverTest, 0, takerTest.Session.CharacterId);
        Assert.That(giverTest.Session.Trade, Is.Not.Null);
        Assert.That(takerTest.Session.Trade, Is.Null, "the one asked has it once the box shows");
        Assert.That(Trade(takerTest), Is.Not.Empty, "the request is shown");

        Send(takerTest, 2, giverTest.Session.CharacterId);
        Assert.Multiple(() => {
            Assert.That(takerTest.Session.Trade, Is.SameAs(giverTest.Session.Trade));
            Assert.That(Trade(giverTest), Does.Contain(Hex(TradePacket.Acknowledge())));
        });
    }

    // The box could not be shown (another box is up): the request ends and the requester is told it timed out.
    [Test]
    public void ABusyAnswer_EndsTheRequest() {
        Send(giverTest, 0, takerTest.Session.CharacterId);
        giverTest.TakeSent();

        Send(takerTest, 14, giverTest.Session.CharacterId);
        Assert.Multiple(() => {
            Assert.That(giverTest.Session.Trade, Is.Null);
            Assert.That(Trade(giverTest), Is.EqualTo(new[] { Hex(TradePacket.Error(s_trade_error_timeout, name: takerTest.Session.PlayerName)) }));
        });
    }

    [Test]
    public void ARequestToAPlayerNotHere_IsDeclined() {
        Send(giverTest, 0, 987654);
        Assert.Multiple(() => {
            Assert.That(giverTest.Session.Trade, Is.Null);
            Assert.That(Trade(giverTest), Is.EqualTo(new[] { Hex(TradePacket.Error(s_trade_error_decline)) }));
        });
    }

    // A box shown for a request that is not there (the requester has none) is declined and gives no trade.
    [Test]
    public void ABoxForNoRequest_IsDeclined() {
        Send(takerTest, 2, giverTest.Session.CharacterId);
        Assert.Multiple(() => {
            Assert.That(takerTest.Session.Trade, Is.Null);
            Assert.That(Trade(takerTest), Is.EqualTo(new[] { Hex(TradePacket.Error(s_trade_error_decline)) }));
        });
    }

    // The one asked, holding the request after its box, cannot answer a second box: it is trading now.
    [Test]
    public void ASecondBox_IsTradingNow() {
        Send(giverTest, 0, takerTest.Session.CharacterId);
        Send(takerTest, 2, giverTest.Session.CharacterId);
        takerTest.TakeSent();

        Send(takerTest, 2, giverTest.Session.CharacterId);
        Assert.That(Trade(takerTest), Is.EqualTo(new[] { Hex(TradePacket.Error(s_trade_error_trading_now)) }));
    }

    // A player already asked by someone else cannot be asked again.
    [Test]
    public void ARequestToAPlayerAlreadyAsked_IsRefused() {
        Send(giverTest, 0, takerTest.Session.CharacterId);
        Send(takerTest, 2, giverTest.Session.CharacterId);
        var thirdTest = new TestGameSession(giverTest.Options);
        thirdTest.Start();
        FieldManager field = giverTest.Session.Field!;
        typeof(Actor<Player>).GetField("<ObjectId>k__BackingField", Any)!.SetValue(thirdTest.Session.Player, 3);
        field.Players[3] = thirdTest.Session.Player;
        thirdTest.Session.Field = field;
        thirdTest.TakeSent();

        Send(thirdTest, 0, takerTest.Session.CharacterId);
        Assert.Multiple(() => {
            Assert.That(thirdTest.Session.Trade, Is.Null);
            Assert.That(Trade(thirdTest), Is.EqualTo(new[] { Hex(TradePacket.Error(s_trade_error_already_request, name: takerTest.Session.PlayerName)) }));
        });
    }
}
