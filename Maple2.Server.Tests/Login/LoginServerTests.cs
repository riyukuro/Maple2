using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Autofac;
using Maple2.Model.Enum;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Network;
using Maple2.Server.Login;
using Maple2.Server.Login.Service;
using Maple2.Server.Login.Session;
using Maple2.Server.World.Service;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Maple2.Server.Tests.Login;

// The login server's session table, built as the host builds it (the banners read from the database, the events from
// the server table), without its listener.
public class LoginServerTests {
    private TestLoginSession test = null!;
    private LoginServer server = null!;

    [SetUp]
    public void SetUp() {
        test = new TestLoginSession();
        server = new LoginServer(new PacketRouter<LoginSession>([]), new ContainerBuilder().Build(), test.Storage, test.Rows.ServerTableMetadata);
    }

    [TearDown]
    public void TearDown() {
        server.Dispose();
    }

    // A session of the test's account, entered on the real server (as ResponseKey's Init does).
    private LoginSession Enter(TestLoginSession owner, long accountId) {
        LoginSession session = owner.Start(init: false);
        TestLoginSession.SetField(session, nameof(LoginSession.Server), server);
        TestLoginSession.SetField(session, "<AccountId>k__BackingField", accountId);
        server.OnConnected(session);
        return session;
    }

    private static bool Open(TestLoginSession owner, LoginSession session) {
        session.Send(Maple2.Server.Core.Packets.NoticePacket.Disconnect(new Maple2.Model.Game.InterfaceText("probe")));
        return owner.TakeSent().Count == 1;
    }

    [Test]
    public void Built_NoSessions_NoBanners_NotStarted() {
        Assert.Multiple(() => {
            Assert.That(server.Port, Is.EqualTo(Target.LoginPort));
            Assert.That(server.GetSessions(), Is.Empty);
            Assert.That(server.GetSystemBanners(), Is.Empty);
            Assert.That(server.GetEvents(), Is.Empty);
            Assert.That(server.CheckHealthAsync(null!).Result.Status, Is.EqualTo(HealthStatus.Unhealthy));
        });
    }

    // A second login of one account replaces the first, which is cut; the first's disconnect keeps the second's entry.
    [Test]
    public void SecondLogin_ReplacesAndCutsTheFirst() {
        var other = new TestLoginSession();
        LoginSession first = Enter(other, 7);
        LoginSession second = Enter(test, 7);

        Assert.Multiple(() => {
            Assert.That(Open(other, first), Is.False);
            Assert.That(server.GetSession(7, out LoginSession? found) ? found : null, Is.SameAs(second));
            Assert.That(server.GetSessions(), Is.EqualTo(new[] { second }));
        });
        second.Dispose();
        Assert.That(server.GetSessions(), Is.Empty);
    }

    // Entering the same session again keeps it.
    [Test]
    public void SameSessionAgain_Kept() {
        LoginSession session = Enter(test, 7);
        server.OnConnected(session);

        Assert.Multiple(() => {
            Assert.That(Open(test, session), Is.True);
            Assert.That(server.GetSessions(), Is.EqualTo(new[] { session }));
        });
        session.Dispose();
    }

    // A stop tells every client, on the character list or still connecting, of the maintenance and closes them all.
    [Test]
    public void Stop_MaintenanceNoticeToEverySession() {
        var connecting = new TestLoginSession();
        LoginSession waiting = connecting.Start(init: false);
        TestLoginSession.SetField(waiting, nameof(LoginSession.Server), server);
        ((HashSet<LoginSession>) typeof(LoginServer).GetField("connectingSessions",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(server)!).Add(waiting);
        var others = new TestLoginSession();
        LoginSession a = Enter(test, 7);
        LoginSession b = Enter(others, 8);

        server.StopAsync(CancellationToken.None).Wait();

        Assert.Multiple(() => {
            foreach ((TestLoginSession owner, LoginSession session) in new[] { (connecting, waiting), (test, a), (others, b) }) {
                Assert.That(owner.TakeSentOps(), Is.EqualTo(new[] { SendOp.Notice }));
                Assert.That(session.State, Is.EqualTo(SessionState.Disconnected));
            }
            Assert.That(server.GetSessions(), Is.Empty);
            Assert.That(server.CheckHealthAsync(null!).Result.Status, Is.EqualTo(HealthStatus.Unhealthy));
        });
    }

    // The world's heartbeat round on the login server: every listed session is asked, and one whose client has been
    // silent for the limit through two unanswered asks is cut.
    [Test]
    public void Heartbeat_AsksEachSession_CutsTheSilentOne() {
        var silent = new TestLoginSession();
        LoginSession answering = Enter(test, 7);
        LoginSession gone = Enter(silent, 8);
        TestLoginSession.SetField(answering, "lastReceivedMs", System.Environment.TickCount64);
        TestLoginSession.SetField(gone, "lastReceivedMs", 0L);
        TestLoginSession.SetField(gone, "unansweredAsks", 2);

        HeartbeatResponse response = new LoginService(server).Heartbeat(new HeartbeatRequest(), null!).Result;

        Assert.Multiple(() => {
            Assert.That(response, Is.Not.Null);
            Assert.That(test.TakeSentOps(), Is.EqualTo(new[] { SendOp.RequestHeartbeat }));
            Assert.That(silent.TakeSent(), Is.Empty);
            Assert.That(gone.State, Is.EqualTo(SessionState.Disconnected));
            Assert.That(answering.UnansweredAsks, Is.EqualTo(1));
        });
        answering.Dispose();
    }
}
