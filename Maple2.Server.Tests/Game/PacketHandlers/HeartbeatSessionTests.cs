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

    [Test]
    public void AClientThatStillSendsIsAskedAgain() {
        session.MarkReceived(Environment.TickCount64 - HeartbeatRules.SilenceLimitMs + 5000);

        Assert.That(Ask().Success, Is.True);
        Assert.That(test.TakeSentOps(), Is.EqualTo(new[] { SendOp.RequestHeartbeat }));
        Assert.That(Closing(), Is.False);
    }

    [Test]
    public void ASilentClientIsCutAndNotAsked() {
        session.MarkReceived(Environment.TickCount64 - HeartbeatRules.SilenceLimitMs);

        Assert.That(Ask().Success, Is.False);
        Assert.That(Closing(), Is.True);
        Assert.That(test.TakeSentOps(), Is.Empty);
    }

    [TestCase(0, false)]
    [TestCase(89999, false)]
    [TestCase(90000, true)]
    public void SilenceCountsFromTheLastPacket(long silentMs, bool expected) {
        Assert.That(HeartbeatRules.IsSilent(1000, 1000 + silentMs), Is.EqualTo(expected));
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

    [Test]
    public void ADisconnectedSessionClosesInOrder() {
        (SocketSession served, Socket peer) = Connect();
        using (peer) {
            served.Disconnect();

            Assert.That(peer.Receive(new byte[16]), Is.EqualTo(0));
        }
    }
}
