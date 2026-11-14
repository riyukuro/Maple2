using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
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

    // Only the events the login screens read go out at the login: the character select loads a lobby map, so a
    // Snowman or DTReward event would open its widget over the character list (their OnStart opens it once the world
    // is loaded). The game server sends them at field entry.
    [Test]
    public void TheLoginSendsOnlyTheLobbyEvents() {
        var test = new TestLoginSession();
        test.AddEvent(9001, GameEventType.DTReward, new DTReward([]));
        test.AddEvent(9002, GameEventType.LobbyMap, new LobbyMap(2000062));
        test.AddEvent(9003, GameEventType.Snowman, new LobbyMap(0));
        LoginSession session = test.Start();
        test.TakeSent();

        session.SendEvents();

        // One event, the LobbyMap: its name first.
        string sent = Convert.ToHexString(test.TakeSent().Single());
        Assert.Multiple(() => {
            Assert.That(sent, Does.StartWith("B200" + "00" + "01000000" + "0800" + Unicode("LobbyMap")));
            Assert.That(sent, Does.Not.Contain(Unicode("DTReward")));
            Assert.That(sent, Does.Not.Contain(Unicode("Snowman")));
            Assert.That(test.Errors, Is.Empty);
        });
    }

    private static string Unicode(string text) => Convert.ToHexString(System.Text.Encoding.Unicode.GetBytes(text));

    [Test]
    public void SentAtLoginTypes() {
        GameEventType[] sent = Enum.GetValues<GameEventType>()
            .Where(type => LoginSession.SentAtLogin(new GameEvent(new GameEventMetadata(1, type, DateTime.MinValue,
                DateTime.MaxValue, TimeSpan.Zero, TimeSpan.Zero, [], new LobbyMap(0), "", "", "", ""))))
            .ToArray();
        Assert.That(sent, Is.EquivalentTo(new[] { GameEventType.LoginNotice, GameEventType.LobbyMap, GameEventType.BurningLevelup }));
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
