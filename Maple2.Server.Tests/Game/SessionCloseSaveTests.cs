using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Maple2.Model.Enum;
using Maple2.Server.Game;
using Maple2.Tools.Scheduler;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Service;
using Maple2.Server.Game.Session;
using Maple2.Server.World.Service;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Maple2.Server.Tests.Game;

// A session's last save and the next entry of its account: a kick from the World is answered only once the kicked
// session's Dispose (its last save) has ended, also for a session that was already closing on another thread, and a
// session tells the World it went offline only after that save.
public class SessionCloseSaveTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private TestGameSession test = null!;
    private GameSession session => test.Session;
    private TimeSpan closeWait;

    [SetUp]
    public void Login() {
        DbContextOptions options = new DbContextOptionsBuilder()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        test = new TestGameSession(options);
        test.Start();
        test.TakeSent();
        closeWait = ChannelService.CloseWait;
        ChannelService.CloseWait = TimeSpan.FromSeconds(10);
    }

    [TearDown]
    public void Restore() {
        ChannelService.CloseWait = closeWait;
    }

    // The test session's server (its events set up by TestGameSession), with the session playing on it.
    private GameServer Server() {
        var server = (GameServer) typeof(GameSession).GetField("server", Any)!.GetValue(session)!;
        var sessions = new ConcurrentDictionary<long, GameSession>();
        sessions[session.CharacterId] = session;
        typeof(GameServer).GetField("sessions", Any)!.SetValue(server, sessions);
        typeof(GameServer).GetField("mutex", Any)!.SetValue(server, new object());
        typeof(GameServer).GetField("connectingSessions", Any)!.SetValue(server, new HashSet<GameSession>());
        return server;
    }

    // A session that plays: its Dispose runs whole (the last save included).
    private GameServer Playing() {
        GameServer server = Server();
        session.GroupChats = new ConcurrentDictionary<int, GroupChatManager>();
        session.Clubs = new ConcurrentDictionary<long, ClubManager>();
        typeof(GameSession).GetField(nameof(GameSession.ItemLockStaging), Any)!.SetValue(session, new long[18]);
        typeof(GameSession).GetField(nameof(GameSession.DismantleStaging), Any)!.SetValue(session, new (long Uid, int Amount)[100]);
        typeof(GameSession).GetField(nameof(GameSession.Scheduler), Any)!.SetValue(session, new EventQueue(Serilog.Log.Logger));
        // No party: its disband at logout sends nothing.
        session.Party = (PartyManager) RuntimeHelpers.GetUninitializedObject(typeof(PartyManager));
        typeof(GameSession).GetField("gameDisposeState", Any)!.SetValue(session, 0);
        typeof(GameSession).GetField("preMigrationSaved", Any)!.SetValue(session, false);
        session.State = SessionState.Connected;
        return server;
    }

    private void ClosingElsewhere() {
        // Another thread closes the session (the client's own close): its Close has begun and its Dispose runs.
        typeof(Maple2.Server.Core.Network.Session).GetField("disconnecting", Any)!.SetValue(session, 1);
        typeof(GameSession).GetField("gameDisposeState", Any)!.SetValue(session, 1);
    }

    private void DisposeEnds() {
        typeof(GameSession).GetField("gameDisposeState", Any)!.SetValue(session, 2);
    }

    private Task<DisconnectResponse> Kick(GameServer server) {
        var channel = new ChannelService(server, null!, test.Storage, null!, null!, null!);
        return Task.Run(() => channel.Disconnect(new DisconnectRequest { CharacterId = session.CharacterId }, null!));
    }

    [Test]
    public void AKickOfASessionClosingElsewhere_IsAnsweredAfterItsDispose() {
        ClosingElsewhere();
        Task<DisconnectResponse> kick = Kick(Server());

        Assert.That(kick.Wait(300), Is.False, "answered while the session's last save still runs");
        DisposeEnds();
        Assert.That(kick.Wait(5000), Is.True);
        Assert.That(kick.Result.Success, Is.True);
    }

    // OnDisconnected takes the session out of the map before its save: the kick still finds it and waits.
    [Test]
    public void AKickFindsASessionThatLeftTheMap_AndWaitsForItsSave() {
        GameServer server = Server();
        ClosingElsewhere();
        server.OnDisconnected(session);
        Assert.That(server.GetSession(session.CharacterId, out _), Is.False);
        Task<DisconnectResponse> kick = Kick(server);

        Assert.That(kick.Wait(300), Is.False, "answered while the session's last save still runs");
        DisposeEnds();
        server.OnClosed(session);
        Assert.That(kick.Wait(5000), Is.True);
        Assert.That(kick.Result.Success, Is.True);
        Assert.That(server.GetSessionOrClosing(session.CharacterId, out _), Is.False);
    }

    // A World heartbeat round during the last save: the player stays online (else the World marks them offline and
    // the next login loads before the save ends); once the save ended the session is unknown.
    [Test]
    public void AHeartbeatDuringTheLastSave_KeepsThePlayerOnline() {
        GameServer server = Server();
        ClosingElsewhere();
        server.OnDisconnected(session);
        var channel = new ChannelService(server, null!, test.Storage, null!, null!, null!);
        var request = new HeartbeatRequest { CharacterId = session.CharacterId };

        Assert.That(channel.Heartbeat(request, null!).Result.Success, Is.True);
        DisposeEnds();
        server.OnClosed(session);
        Assert.That(channel.Heartbeat(request, null!).Result.Success, Is.False);
    }

    // The old session of a character whose newer session already plays here: its close leaves the newer one in
    // place and is not kept as closing.
    [Test]
    public void TheCloseOfAnOldSession_LeavesTheNewerOne() {
        GameServer server = Server();
        var sessions = (ConcurrentDictionary<long, GameSession>) typeof(GameServer).GetField("sessions", Any)!.GetValue(server)!;
        var newer = (GameSession) RuntimeHelpers.GetUninitializedObject(typeof(GameSession));
        GC.SuppressFinalize(newer); // Never built: its Dispose must not run on the finalizer thread.
        sessions[session.CharacterId] = newer;

        server.OnDisconnected(session);
        Assert.That(server.GetSession(session.CharacterId, out GameSession? found), Is.True);
        Assert.That(found, Is.SameAs(newer));
        var closing = (ConcurrentDictionary<long, GameSession>?) typeof(GameServer).GetField("closing", Any)!.GetValue(server);
        Assert.That(closing?.ContainsKey(session.CharacterId) ?? false, Is.False);
    }

    [Test]
    public void AKickOfAPlayingSession_RunsItsDispose() {
        GameServer server = Playing();

        DisconnectResponse response = Kick(server).Result;

        Assert.That(response.Success, Is.True);
        Assert.That(session.WaitDisposed(TimeSpan.Zero), Is.True);
        Assert.That(server.GetSessionOrClosing(session.CharacterId, out _), Is.False);
    }

    // The logout's offline update reaches the World after the last save took the account lock (and released it).
    [Test]
    public void ALogout_TellsTheWorldItWentOfflineAfterItsLastSave() {
        GameServer server = Playing();
        // Off the map: the harness field is only half built, and the save needs none.
        session.Field = null;
        int offlineAtSave = -1;
        test.World.OnAcquire = () => offlineAtSave = test.World.PlayerUpdates.Count(update => update.Channel == -1);

        session.Dispose();

        Assert.That(offlineAtSave, Is.EqualTo(0));
        Assert.That(test.World.PlayerUpdates.Count(update => update.Channel == -1), Is.EqualTo(1));
        Assert.That(server.GetSessionOrClosing(session.CharacterId, out _), Is.False);
        // The harness leaves the guild and the field player half set (their own dispose logs); the cleanup with the
        // save and the logout update log nothing.
        Assert.That(test.Errors.Where(error => error.Contains("session cleanup") || error.Contains("logout")), Is.Empty);
    }
}
