using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Helpers;
using Maple2.Server.Core.Network;
using Maple2.Server.Game;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Service;
using Maple2.Server.Game.Session;
using Maple2.Server.World.Service;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// What ends a session over heartbeats: not a late answer, only a client that has sent nothing for the limit; and
// how the socket of a session that is cut closes.
public class HeartbeatSessionTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private TestGameSession test = null!;
    private GameSession session => test.Session;

    [SetUp]
    public void Login() {
        DbContextOptions options = new DbContextOptionsBuilder()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        test = new TestGameSession(options);
        test.Start();
        test.TakeSent();
    }

    private bool Closing() {
        return (int) typeof(Session).GetField("disconnecting", Any)!.GetValue(session)! == 1;
    }

    private HeartbeatResponse Ask() {
        var server = (GameServer) RuntimeHelpers.GetUninitializedObject(typeof(GameServer));
        var sessions = new ConcurrentDictionary<long, GameSession>();
        sessions[session.CharacterId] = session;
        typeof(GameServer).GetField("sessions", Any)!.SetValue(server, sessions);
        var channel = new ChannelService(server, null!, test.Storage, null!, null!, null!);
        return channel.Heartbeat(new HeartbeatRequest { CharacterId = session.CharacterId }, null!).GetAwaiter().GetResult();
    }

    [TestCase(100)]
    [TestCase(3000)]
    [TestCase(60000)]
    public void ALateAnswerDoesNotEndTheSession(int lateMs) {
        new ResponseHeartbeatHandler().Handle(session, TestGameSession.Packet(writer => {
            writer.WriteInt(Environment.TickCount - lateMs);
            writer.WriteInt(12345);
        }));

        Assert.That(Closing(), Is.False);
    }

    // The tick sync answer (server int back, client elapsed time) is read whole and answered with nothing.
    [Test]
    public void TheTickSyncAnswerIsReadAndDropped() {
        var packet = TestGameSession.Packet(writer => {
            writer.WriteInt(Environment.TickCount);
            writer.WriteInt(12345);
        });

        new ResponseClientTickSyncHandler().Handle(session, packet);

        Assert.That(packet.Available, Is.Zero);
        Assert.That(test.TakeSentOps(), Is.Empty);
        Assert.That(Closing(), Is.False);
    }

    [Test]
    public void AClientThatStillSendsIsAskedAgain() {
        session.MarkReceived(Environment.TickCount64 - HeartbeatRules.SilenceLimitMs + 5000);

        Assert.That(Ask().Success, Is.True);
        Assert.That(test.TakeSentOps(), Is.EqualTo(new[] { SendOp.RequestHeartbeat }));
        Assert.That(Closing(), Is.False);
    }

    [Test]
    public void ASilentClientIsCutAndNotAsked() {
        long now = Environment.TickCount64;
        session.MarkReceived(now - HeartbeatRules.SilenceLimitMs);
        Assert.That(HeartbeatRules.Ask(session, now - 2 * HeartbeatRules.IntervalMs), Is.True);
        Assert.That(HeartbeatRules.Ask(session, now - HeartbeatRules.IntervalMs), Is.True);
        test.TakeSentOps();

        Assert.That(Ask().Success, Is.False);
        Assert.That(Closing(), Is.True);
        Assert.That(test.TakeSentOps(), Is.Empty);
    }

    [TestCase(0, 2, false)]
    [TestCase(89999, 2, false)]
    [TestCase(90000, 2, true)]
    [TestCase(90000, 3, true)]
    [TestCase(90000, 1, false)]
    [TestCase(600000, 0, false)]
    public void SilenceCountsFromTheLastPacketAndNeedsUnansweredAsks(long silentMs, int unanswered, bool expected) {
        Assert.That(HeartbeatRules.IsSilent(1000, unanswered, 1000 + silentMs), Is.EqualTo(expected));
    }

    // The rounds stopped for a while (the World server restarted, or its round was held up): the client had
    // nothing to answer, so the first rounds afterwards ask it and an answer keeps the session.
    [Test]
    public void AHeldUpRoundDoesNotCutAnIdleSession() {
        long now = Environment.TickCount64;
        session.MarkReceived(now - 200000);

        Assert.That(HeartbeatRules.Ask(session, now), Is.True, "asked first");
        Assert.That(HeartbeatRules.Ask(session, now + 1), Is.True);
        Assert.That(HeartbeatRules.Ask(session, now + 2), Is.True, "rounds in a burst count as one ask");
        Assert.That(session.UnansweredAsks, Is.EqualTo(1));
        Assert.That(Closing(), Is.False);

        session.MarkReceived(now + 100);
        Assert.That(session.UnansweredAsks, Is.EqualTo(0));
        Assert.That(HeartbeatRules.Ask(session, now + HeartbeatRules.IntervalMs), Is.True);
        Assert.That(Closing(), Is.False);
    }

    [Test]
    public void AfterAHeldUpRoundASilentClientIsCutByTheThirdRound() {
        long now = Environment.TickCount64;
        session.MarkReceived(now - 200000);

        Assert.That(HeartbeatRules.Ask(session, now), Is.True);
        Assert.That(HeartbeatRules.Ask(session, now + HeartbeatRules.IntervalMs), Is.True);
        Assert.That(HeartbeatRules.Ask(session, now + 2 * HeartbeatRules.IntervalMs), Is.False);
        Assert.That(Closing(), Is.True);
    }

    private sealed class SocketSession : Session {
        protected override PatchType Type => PatchType.Ignore;
        public SocketSession(TcpClient client) : base(client) { }
    }

    private static (SocketSession, Socket) Connect() {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var peer = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        peer.Connect((IPEndPoint) listener.LocalEndpoint);
        var served = new SocketSession(listener.AcceptTcpClient());
        listener.Stop();
        peer.ReceiveTimeout = 10000;
        return (served, peer);
    }

    // The client shows its lost-connection notice only for a close with an error.
    [Test]
    public void ACutSessionResetsTheSocket() {
        (SocketSession served, Socket peer) = Connect();
        using (peer) {
            served.Cut();

            var error = Assert.Throws<SocketException>(() => peer.Receive(new byte[16]));
            Assert.That(error!.SocketErrorCode, Is.EqualTo(SocketError.ConnectionReset));
        }
    }

    // An error the client shows but does not leave on: the packet arrives, then the reset.
    [Test]
    public void ACutAfterSendDeliversTheLastPacketAndThenResets() {
        (SocketSession served, Socket peer) = Connect();
        using (peer) {
            var reader = System.Threading.Tasks.Task.Run(() => {
                int total = 0;
                var buffer = new byte[256];
                try {
                    while (true) {
                        int read = peer.Receive(buffer);
                        if (read == 0) {
                            return (total, SocketError.Success);
                        }
                        total += read;
                    }
                } catch (SocketException ex) {
                    return (total, ex.SocketErrorCode);
                }
            });
            byte[] last = new byte[40];
            served.Send(last);
            served.CutAfterSend();

            (int received, SocketError end) = reader.GetAwaiter().GetResult();
            Assert.That(received, Is.GreaterThanOrEqualTo(last.Length), "the last packet was read");
            Assert.That(end, Is.EqualTo(SocketError.ConnectionReset));
        }
    }

    [Test]
    public void ADisconnectedSessionClosesInOrder() {
        (SocketSession served, Socket peer) = Connect();
        using (peer) {
            served.Disconnect();

            Assert.That(peer.Receive(new byte[16]), Is.EqualTo(0));
        }
    }

    // The first close decides: a Cut that comes while an orderly close is under way (the session's own threads
    // end with one) does not turn it into a reset.
    [Test]
    public void ACutAfterADisconnectStaysInOrder() {
        (SocketSession served, Socket peer) = Connect();
        using (peer) {
            served.Disconnect();
            served.Cut();

            Assert.That(peer.Receive(new byte[16]), Is.EqualTo(0));
        }
    }

    [Test]
    public void ADisconnectAfterACutStaysAReset() {
        (SocketSession served, Socket peer) = Connect();
        using (peer) {
            served.Cut();
            served.Disconnect();

            var error = Assert.Throws<SocketException>(() => peer.Receive(new byte[16]));
            Assert.That(error!.SocketErrorCode, Is.EqualTo(SocketError.ConnectionReset));
        }
    }

    // The round the login server runs for its sessions.
    [Test]
    public void ASilentSessionOfAnyServerIsReset() {
        (SocketSession served, Socket peer) = Connect();
        using (peer) {
            long now = Environment.TickCount64;
            served.MarkReceived(now - HeartbeatRules.SilenceLimitMs);
            served.MarkAsked(now - 2 * HeartbeatRules.IntervalMs, HeartbeatRules.AskGapMs);
            served.MarkAsked(now - HeartbeatRules.IntervalMs, HeartbeatRules.AskGapMs);

            Assert.That(HeartbeatRules.Ask(served, now), Is.False);
            var error = Assert.Throws<SocketException>(() => peer.Receive(new byte[16]));
            Assert.That(error!.SocketErrorCode, Is.EqualTo(SocketError.ConnectionReset));
        }
    }

    [Test]
    public void ASessionOfAnyServerThatStillSendsIsKept() {
        (SocketSession served, Socket peer) = Connect();
        using (peer) {
            long now = Environment.TickCount64;
            served.MarkReceived(now - HeartbeatRules.SilenceLimitMs + 1);

            Assert.That(HeartbeatRules.Ask(served, now), Is.True);
            Assert.That((int) typeof(Session).GetField("disconnecting", Any)!.GetValue(served)!, Is.EqualTo(0));
            served.Disconnect();
        }
    }
}
