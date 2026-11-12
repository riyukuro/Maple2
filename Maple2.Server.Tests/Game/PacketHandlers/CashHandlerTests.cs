using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// CashHandler: the cash shop window's requests when it opens, and the packets as the client reads them.
public class CashHandlerTests {
    private readonly CashHandler handler = new();

    private static List<string> Sent(TestGameSession test) {
        return test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.Cash)
            .Select(packet => Convert.ToHexString(packet)[4..]).ToList();
    }

    // The window's three requests in the client's order: balance, products, page 1 of the bought products.
    [Test]
    public void Open_AnswersEachRequest() {
        var test = new TestGameSession();
        GameSession session = test.Start();
        test.TakeSent();

        handler.Handle(session, TestGameSession.Packet(writer => writer.WriteByte(1)));
        handler.Handle(session, TestGameSession.Packet(writer => writer.WriteByte(0)));
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(5);
            writer.WriteInt(1);
        }));

        Assert.That(Sent(test), Is.EqualTo(new[] {
            "01" + "01000000" + "00000000",
            "00" + "00000000",
            "05" + "01000000" + "01000000" + "00000000",
        }));
    }

    // A page button: the answer names the page asked for.
    [Test]
    public void Inventory_AnswersThePageAskedFor() {
        var test = new TestGameSession();
        GameSession session = test.Start();
        test.TakeSent();

        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(5);
            writer.WriteInt(3);
        }));

        Assert.That(Sent(test), Is.EqualTo(new[] { "05" + "01000000" + "03000000" + "00000000" }));
    }

    // A request that needs a listed product gets no answer and no error.
    [Test]
    public void Buy_IsNotAnswered() {
        var test = new TestGameSession();
        GameSession session = test.Start();
        test.TakeSent();

        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(2);
            writer.WriteInt(30000000);
            writer.WriteInt(1);
        }));

        Assert.That(Sent(test), Is.Empty);
        Assert.That(test.Errors, Is.Empty);
    }

    // The login flag as KMS2 sends it.
    [Test]
    public void EnabledPacket() {
        Assert.That(Convert.ToHexString(CashPacket.Enabled(true).ToArray()), Is.EqualTo("AD00" + "09" + "01"));
        Assert.That(Convert.ToHexString(CashPacket.Enabled(false).ToArray()), Is.EqualTo("AD00" + "09" + "00"));
    }
}
