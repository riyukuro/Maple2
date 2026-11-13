using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Maple2.Model.Game.Event;
using Maple2.Model.Validators;
using Maple2.Server.Core.Constants;
using Maple2.Server.Login.PacketHandlers;
using Maple2.Server.Login.Session;
using Maple2.Server.Tests.Game;

namespace Maple2.Server.Tests.Login;

public class NoticeDialogTests {
    // The login notice's packet (0x7a, mode 1) is read and answered with nothing; the session stays.
    [Test]
    public void TheLoginNoticeIsReadAndNotAnswered() {
        var test = new TestLoginSession();
        LoginSession session = test.Start();
        var handler = new NoticeDialogHandler();

        handler.Handle(session, TestGameSession.Packet(writer => writer.WriteByte(1)));

        Assert.Multiple(() => {
            Assert.That(handler.OpCode, Is.EqualTo(RecvOp.NoticeDialog));
            Assert.That((ushort) handler.OpCode, Is.EqualTo(0x7A));
            Assert.That(test.TakeSent(), Is.Empty);
            Assert.That(test.Errors, Is.Empty);
            Assert.That(session.Server.GetSession(test.AccountId, out _), Is.True);
        });
    }

    private const string LoginNoticeEvent = "0B00" + "4C006F00670069006E004E006F007400690063006500" + "01752B7D";

    // The login's event list (GameEvent 0xb2, Load): the world's login notice adds a LoginNotice, which turns
    // CGameRule::IsLoginNoticeOn on and so opens UILoginNotice in the lobby.
    [TestCase(true, "B200" + "00" + "01000000" + LoginNoticeEvent)]
    [TestCase(false, "B200" + "00" + "00000000")]
    public void TheLoginNoticeIsAnEventWhileOn(bool on, string expected) {
        var test = new TestLoginSession();
        test.World.NoticeLogin = on;
        LoginSession session = test.Start();
        test.TakeSent();

        session.SendEvents();

        Assert.Multiple(() => {
            Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { expected }));
            Assert.That(test.World.Admins.Select(request => (request.Notice.Kind, request.Notice.Action)),
                Is.EqualTo(new[] { (NoticeRules.Login, NoticeRules.Status) }));
            Assert.That(test.Errors, Is.Empty);
        });
    }

    // A LoginNotice of the event table is sent as it is; the world is not asked and none is added.
    [Test]
    public void ATableLoginNoticeIsNotSentTwice() {
        var test = new TestLoginSession();
        test.World.NoticeLogin = true;
        LoginSession session = test.Start();
        test.TakeSent();
        var events = (Dictionary<int, GameEvent>) typeof(Maple2.Server.Login.LoginServer)
            .GetField("eventCache", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session.Server)!;
        GameEvent table = NoticeRules.LoginNoticeEvent(DateTime.Now);
        events[table.Id] = table;

        session.SendEvents();

        Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { "B200" + "00" + "01000000" + LoginNoticeEvent }));
        Assert.That(test.World.Admins, Is.Empty);
    }

    // ServerEnter sends the list with the login notice.
    [Test]
    public void ServerEnterSendsTheLoginNotice() {
        var test = new TestLoginSession();
        test.World.NoticeLogin = true;
        LoginSession session = test.Start();
        test.TakeSent();

        new ServerEnterHandler().Handle(session, TestGameSession.Packet(writer => writer.WriteInt(1)));

        List<byte[]> events = test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.GameEvent).ToList();
        Assert.That(events.Select(Convert.ToHexString), Is.EqualTo(new[] { "B200" + "00" + "01000000" + LoginNoticeEvent }));
    }
}
