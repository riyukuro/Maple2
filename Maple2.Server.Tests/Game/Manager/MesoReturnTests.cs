using System;
using System.Collections.Generic;
using System.Threading;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.Manager;

// The one-step meso changes: a take against a gain on another thread, a take of more than is held, and the
// give-back of a take made without its packet (buy back).
public class MesoReturnTests {
    private static ushort Op(byte[] packet) => BitConverter.ToUInt16(packet, 0);

    // A take on the packet thread while the field's thread adds: none is lost.
    [Test]
    public void TakeAgainstGain_NoChangeLost() {
        var test = new TestGameSession();
        GameSession session = test.Start();
        const int Count = 20000;
        session.Currency.Meso = Count;
        test.TakeSent();
        using var start = new Barrier(2);
        var other = new Thread(() => {
            start.SignalAndWait();
            for (int i = 0; i < Count; i++) {
                session.Currency.AddMeso(1, ExpMessageCode.s_msg_take_exp);
            }
        });
        other.Start();
        start.SignalAndWait();
        for (int i = 0; i < Count; i++) {
            session.Currency.ChangeMeso(-1);
        }
        other.Join();

        Assert.That(session.Currency.Meso, Is.EqualTo((long) Count));
    }

    // A take of more than is held throws and changes nothing (no packet).
    [Test]
    public void TakeOverTheBalance_Throws() {
        var test = new TestGameSession();
        GameSession session = test.Start();
        session.Currency.Meso = 5;
        test.TakeSent();

        Assert.Throws<ArgumentException>(() => session.Currency.ChangeMeso(-6));
        Assert.Multiple(() => {
            Assert.That(session.Currency.Meso, Is.EqualTo(5));
            Assert.That(test.TakeSent(), Is.Empty);
        });
    }

    // The give-back sends the balance: a packet sent between the take and the give-back showed the balance without
    // the price. Before, nothing was sent.
    [Test]
    public void ReturnMeso_SendsTheBalance() {
        var test = new TestGameSession();
        GameSession session = test.Start();
        session.Currency.Meso = 100;
        Assert.That(session.Currency.TryTakeMeso(40, notify: false), Is.True);
        session.Currency.AddMeso(1, ExpMessageCode.s_msg_take_exp);
        test.TakeSent();

        session.Currency.ReturnMeso(40);
        List<byte[]> sent = test.TakeSent();

        Assert.Multiple(() => {
            Assert.That(session.Currency.Meso, Is.EqualTo(101));
            Assert.That(sent.ConvertAll(Op), Is.EqualTo(new[] { (ushort) SendOp.Meso }));
        });
    }

    // A give-back that reaches the cap stops there (the part over it is logged) and does not overflow.
    [Test]
    public void ReturnMeso_AtTheCap_StopsThere() {
        var test = new TestGameSession();
        GameSession session = test.Start();
        session.Currency.Meso = Constant.MaxMeso - 10;
        Assert.That(session.Currency.TryTakeMeso(40, notify: false), Is.True);
        session.Currency.AddMeso(45, ExpMessageCode.s_msg_take_exp);

        session.Currency.ReturnMeso(40);

        Assert.That(session.Currency.Meso, Is.EqualTo(Constant.MaxMeso));
    }
}
