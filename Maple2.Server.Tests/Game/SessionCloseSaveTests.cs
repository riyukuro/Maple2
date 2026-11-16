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
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers.Field;
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
    private TimeSpan packetBatchWait;
    private TimeSpan sessionCloseWait;

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
        packetBatchWait = GameSession.PacketBatchWait;
        sessionCloseWait = GameSession.CloseWait;
    }

    [TearDown]
    public void Restore() {
        ChannelService.CloseWait = closeWait;
        GameSession.PacketBatchWait = packetBatchWait;
        GameSession.CloseWait = sessionCloseWait;
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

    private object InlineGate() {
        return typeof(Maple2.Server.Core.Network.Session).GetProperty("InlineGate", Any)!.GetValue(session)!;
    }

    private void SetPlayer(FieldPlayer? player) {
        typeof(GameSession).GetField("<Player>k__BackingField", Any)!.SetValue(session, player);
    }

    // A kick (a login of the account elsewhere) while the session still loads its character (EnterServer, a handler
    // on the receive thread under the gate): the close waits for the load and ends the session it made the whole
    // way, the last save included, and the kick is answered after that. Before, the close took the path of a session
    // without a character at once and the kick was answered while the load went on: the session it made played on
    // with no close left to end it.
    [Test]
    public void AKickDuringTheCharactersLoad_WaitsForIt_AndEndsTheLoadedSession() {
        GameServer server = Playing();
        session.Field = null;
        FieldPlayer player = session.Player;
        SetPlayer(null);
        session.State = SessionState.ChangeMap; // as EnterServer sets it before the load
        using var loading = new ManualResetEventSlim();
        using var loaded = new ManualResetEventSlim();
        Task load = Task.Run(() => {
            lock (InlineGate()) {
                loading.Set();
                loaded.Wait();
                SetPlayer(player);
            }
        });
        loading.Wait();
        int locksBefore = test.World.Acquired;

        Task<DisconnectResponse> kick = Kick(server);
        Assert.That(kick.Wait(300), Is.False, "answered while the character's load still runs");
        loaded.Set();
        Assert.That(load.Wait(5000), Is.True);
        Assert.That(kick.Wait(5000), Is.True);

        Assert.That(kick.Result.Success, Is.True);
        Assert.That(session.WaitDisposed(TimeSpan.Zero), Is.True);
        Assert.That(player.Value.Character.Channel, Is.EqualTo(-1), "the loaded session was not ended");
        Assert.That(player.Value.Account.Online, Is.False);
        Assert.That(test.World.Acquired, Is.GreaterThan(locksBefore), "no last save");
        Assert.That(server.GetSessionOrClosing(session.CharacterId, out _), Is.False);
    }

    // A kick of a session that has not begun its load: no load starts after the close (the character's packet that
    // arrives later is dropped).
    [Test]
    public void AKickBeforeTheCharactersLoad_ClosesItsPackets() {
        GameServer server = Playing();
        session.Field = null;
        SetPlayer(null);
        session.State = SessionState.ChangeMap;

        DisconnectResponse response = Kick(server).Result;

        Assert.That(response.Success, Is.True);
        Assert.That(session.WaitDisposed(TimeSpan.Zero), Is.True);
        Assert.That(session.DropsPackets, Is.True);
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

    // A stand-in for the field's loop on a thread of its own, as the field's: first a handler of the player, in a
    // packet batch, runs until it is let go; then the loop runs the field's queued actions until stopped.
    private sealed class FieldLoop : IDisposable {
        public readonly ManualResetEventSlim InHandler = new();
        public readonly ManualResetEventSlim Release = new();
        private readonly CancellationTokenSource cancel = new();
        private readonly Thread thread;
        public bool LeftOnItsThread;

        public FieldLoop(FieldManager field, int objectId, Action handlerStart, Action handlerEnd) {
            var batch = new List<(FieldPacketHandler, GameSession, Maple2.PacketLib.Tools.ByteReader)>();
            typeof(FieldManager).GetField("queuedPackets", Any)!.SetValue(field, batch);
            typeof(FieldManager).GetField("cancel", Any)!.SetValue(field, cancel);
            thread = new Thread(() => {
                lock (batch) {
                    handlerStart();
                    InHandler.Set();
                    Release.Wait();
                    handlerEnd();
                }
                while (!cancel.IsCancellationRequested) {
                    bool before = field.Players.ContainsKey(objectId);
                    field.RunFieldActions();
                    LeftOnItsThread |= before && !field.Players.ContainsKey(objectId);
                    Thread.Sleep(5);
                }
            });
            typeof(FieldManager).GetField("thread", Any)!.SetValue(field, thread);
            thread.Start();
            InHandler.Wait();
        }

        // The field is disposed: its loop ends after the handler, without running the actions queued meanwhile.
        public void Stop() => cancel.Cancel();

        public void Dispose() {
            Release.Set();
            cancel.Cancel();
            thread.Join();
        }
    }

    // The members of the half-built test field and session that the field's RemovePlayer reads.
    private void Leavable(FieldManager field) {
        session.Ride = new RideManager(session);
        typeof(FieldManager).GetField("fieldReactors", Any)!.SetValue(field, new ConcurrentDictionary<int, Maple2.Server.Game.Model.FieldReactor>());
        typeof(FieldManager).GetField("<Rps>k__BackingField", Any)!.SetValue(field, new Maple2.Model.Validators.RpsMatchTable());
        typeof(FieldManager).GetField("<CoupleDance>k__BackingField", Any)!.SetValue(field, new Maple2.Model.Validators.CoupleDanceMatchTable());
    }

    private (long Meso, long Meret) StoredWallets() {
        using var context = new Maple2.Database.Context.Ms2Context(test.Options);
        long meso = context.Character.AsNoTracking().Single(row => row.Id == session.CharacterId).Currency.Meso;
        long meret = context.Account.AsNoTracking().Single(row => row.Id == session.AccountId).Currency.Meret;
        return (meso, meret);
    }

    // A close while a handler of the player still runs on the field thread past PacketBatchWait: the field is left on
    // the field's thread once the handler ended, and the last save stores the handler's change whole. Before, the close
    // went on beside it after PacketBatchWait: the field left on the closing thread (the field's update reading the
    // buffs and the pet changed under it) and a save that stored the change half made.
    [Test]
    public void TheClose_WaitsForTheFieldThreadsHandler_AndLeavesTheFieldOnIt() {
        Playing();
        session.Currency.Meso = 1000;
        session.Currency.Meret = 10;
        GameSession.PacketBatchWait = TimeSpan.FromMilliseconds(100);
        GameSession.CloseWait = TimeSpan.FromSeconds(30);
        Leavable(session.Field!);
        using var loop = new FieldLoop(session.Field!, session.Player.ObjectId,
            () => session.Currency.Meso = 2000, () => session.Currency.Meret = 20);

        Task close = Task.Run(session.Dispose);
        Assert.That(close.Wait(1000), Is.False, "the close went on beside the running handler");
        loop.Release.Set();
        Assert.That(close.Wait(TimeSpan.FromSeconds(30)), Is.True);

        Assert.Multiple(() => {
            Assert.That(loop.LeftOnItsThread, Is.True, "the field was not left on the field's thread");
            Assert.That(StoredWallets(), Is.EqualTo((2000L, 20L)));
            Assert.That(test.Errors.Where(error => error.Contains("session cleanup")), Is.Empty);
        });
    }

    // A field thread stuck past CloseWait: the close leaves the field beside it (logged), and the queued leave does
    // nothing once the loop runs again. Until then the next close on the field does not wait again (a server stop
    // closes its sessions one by one); once the loop ran its actions, a close waits for it again.
    [Test]
    public void AFieldThreadStuckPastTheWait_IsLeftBesideIt_Once() {
        Playing();
        FieldManager field = session.Field!;
        using var loop = new FieldLoop(field, session.Player.ObjectId, () => { }, () => { });
        int runs = 0;
        bool OnTheFieldThread() => field.OnFieldThread;

        bool onField = field.RunOnFieldAndWait(TimeSpan.FromMilliseconds(200), () => Interlocked.Increment(ref runs));
        var next = System.Diagnostics.Stopwatch.StartNew();
        bool nextOnField = field.RunOnFieldAndWait(TimeSpan.FromSeconds(30), () => Interlocked.Increment(ref runs));
        next.Stop();
        loop.Release.Set();
        Thread.Sleep(200);
        bool after = false;
        bool afterOnField = field.RunOnFieldAndWait(TimeSpan.FromSeconds(30), () => after = OnTheFieldThread());

        Assert.Multiple(() => {
            Assert.That(onField, Is.False);
            Assert.That(nextOnField, Is.False);
            Assert.That(next.Elapsed, Is.LessThan(TimeSpan.FromSeconds(5)), "the next close waited for the stuck thread again");
            Assert.That(runs, Is.EqualTo(2));
            Assert.That(afterOnField, Is.True);
            Assert.That(after, Is.True, "after the loop ran again the action did not run on the field's thread");
        });
    }

    // A field disposed while a close waits for its thread (its loop ends without running the queued leave): the close
    // goes on at once, not after the whole CloseWait.
    [Test]
    public void AFieldStoppingWhileACloseWaits_EndsTheWait() {
        Playing();
        FieldManager field = session.Field!;
        using var loop = new FieldLoop(field, session.Player.ObjectId, () => { }, () => { });
        int runs = 0;

        Task<bool> wait = Task.Run(() => field.RunOnFieldAndWait(TimeSpan.FromSeconds(30), () => Interlocked.Increment(ref runs)));
        Assert.That(wait.Wait(300), Is.False);
        loop.Stop();

        Assert.That(wait.Wait(TimeSpan.FromSeconds(5)), Is.True, "the close waited on for a field that stopped");
        Assert.That(runs, Is.EqualTo(1));
    }

    // A GM kick (a command, on the field thread) whose target's close stalls: the kick is asked off the field's
    // thread, so the handler ends at once and the field runs on while the World has not answered. Before, the handler
    // waited for the World's answer, holding the field (every player on it) for up to the World's kick wait.
    [Test]
    public void AStalledTargetClose_DoesNotHoldTheGmsField() {
        Playing();
        FieldManager field = session.Field!;
        using var stall = new ManualResetEventSlim();
        test.World.OnDisconnect = _ => {
            stall.Wait(TimeSpan.FromSeconds(10));
            return new DisconnectResponse { Success = true };
        };
        Task<DisconnectResponse?>? kick = null;
        var handler = new System.Diagnostics.Stopwatch();

        using var loop = new FieldLoop(field, session.Player.ObjectId, () => {
            handler.Start();
            kick = session.Kick(new DisconnectRequest { CharacterId = 999, Force = true });
            handler.Stop();
        }, () => { });
        loop.Release.Set();
        bool onItsThread = false;
        bool ran = field.RunOnFieldAndWait(TimeSpan.FromSeconds(5), () => onItsThread = field.OnFieldThread);
        bool answeredMeanwhile = kick!.IsCompleted;
        stall.Set();

        Assert.Multiple(() => {
            Assert.That(handler.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)), "the handler waited for the World's answer");
            Assert.That(ran && onItsThread, Is.True, "the field did not run while the kick waited");
            Assert.That(answeredMeanwhile, Is.False);
            Assert.That(kick.Wait(TimeSpan.FromSeconds(5)) && kick.Result?.Success == true, Is.True);
        });
    }

    // A player of the GM's field closing by themselves while the GM's kick of someone else waits for the World: the
    // close still waits for the packet batch the field's thread runs and leaves the field on that thread, so the last
    // save stores the handler's change whole. Before, the kick marked the field as waiting on it, and that close went
    // on beside the field's thread at once.
    [Test]
    public void AVoluntaryCloseDuringAGmKick_StillTakesTheBatchLock() {
        Playing();
        Leavable(session.Field!);
        session.Currency.Meso = 4000;
        session.Currency.Meret = 40;
        GameSession.PacketBatchWait = TimeSpan.FromSeconds(30);
        GameSession.CloseWait = TimeSpan.FromSeconds(30);
        using var asked = new ManualResetEventSlim();
        using var stall = new ManualResetEventSlim();
        test.World.OnDisconnect = _ => {
            asked.Set();
            stall.Wait(TimeSpan.FromSeconds(10));
            return new DisconnectResponse { Success = true };
        };
        Task<FieldLoop> start = Task.Run(() => new FieldLoop(session.Field!, session.Player.ObjectId,
            () => session.Kick(new DisconnectRequest { CharacterId = 999, Force = true }), () => session.Currency.Meret = 41));
        try {
            Assert.That(asked.Wait(TimeSpan.FromSeconds(5)), Is.True);
            Task close = Task.Run(session.Dispose);
            Assert.That(close.Wait(300), Is.False, "the close went on beside the field's packet batch");
            Assert.That(start.Wait(TimeSpan.FromSeconds(5)), Is.True);
            start.Result.Release.Set();

            Assert.Multiple(() => {
                Assert.That(close.Wait(TimeSpan.FromSeconds(10)), Is.True);
                Assert.That(start.Result.LeftOnItsThread, Is.True);
                Assert.That(StoredWallets(), Is.EqualTo((4000L, 41L)));
            });
        } finally {
            stall.Set();
            start.Wait(TimeSpan.FromSeconds(15));
            if (start.IsCompletedSuccessfully) {
                start.Result.Dispose();
            }
        }
    }

    // A GM kicking their own session: the kick is answered once the close ended, and that close left the field on the
    // field's thread and saved.
    [Test]
    public void AGmKickingThemselves_IsAnsweredAfterTheirClose() {
        Playing();
        Leavable(session.Field!);
        session.Currency.Meso = 3000;
        session.Currency.Meret = 30;
        GameSession.PacketBatchWait = TimeSpan.FromSeconds(30);
        GameSession.CloseWait = TimeSpan.FromSeconds(30);
        bool closed = false;
        test.World.OnDisconnect = _ => {
            closed = Task.Run(session.Dispose).Wait(TimeSpan.FromSeconds(10));
            return new DisconnectResponse { Success = closed };
        };
        Task<DisconnectResponse?>? kick = null;

        using var loop = new FieldLoop(session.Field!, session.Player.ObjectId,
            () => kick = session.Kick(new DisconnectRequest { CharacterId = session.CharacterId, Force = true }), () => { });
        loop.Release.Set();

        Assert.Multiple(() => {
            Assert.That(kick!.Wait(TimeSpan.FromSeconds(15)), Is.True);
            Assert.That(closed, Is.True);
            Assert.That(kick.Result?.Success, Is.True);
            Assert.That(loop.LeftOnItsThread, Is.True);
            Assert.That(StoredWallets(), Is.EqualTo((3000L, 30L)));
            Assert.That(test.Errors.Where(error => error.Contains("session cleanup")), Is.Empty);
        });
    }
}
