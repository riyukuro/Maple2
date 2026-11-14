using System;
using System.Collections;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Maple2.Model.Enum;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Network;
using Maple2.Server.Core.PacketHandlers;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game;

// The channel change and the quit to the character select save the session for the move. They run on the field
// thread, after every packet the player sent before them, and the field queue handles no packet of the session after
// that save: a reward claimed after it would keep its item (an item row is stored when it is made) while its claim is
// not stored, and the next server loads the saved state.
public class MoveSaveQueueTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private sealed class Recorder : FieldPacketHandler {
        public override RecvOp OpCode => RecvOp.RequestQuit;
        public int Handled;
        public override void Handle(GameSession session, IByteReader packet) => Handled++;
    }

    // A handler that runs on the receive thread (as the guild fund donation does).
    private sealed class InlineRecorder : PacketHandler<GameSession> {
        public override RecvOp OpCode => RecvOp.Guild;
        public int Handled;
        public override void Handle(GameSession session, IByteReader packet) => Handled++;
    }

    // The test session starts as saved and closed (TestGameSession.Start); these play on as a live session does.
    private static GameSession Unsaved(TestGameSession test) {
        GameSession session = test.Start();
        typeof(GameSession).GetField("preMigrationSaved", Any)!.SetValue(session, false);
        session.State = SessionState.Connected;
        return session;
    }

    private static void Queue(GameSession session, FieldPacketHandler handler, Action<ByteWriter> write) {
        Assert.That(handler.TryHandleDeferred(session, TestGameSession.Packet(write)), Is.True);
    }

    // The channel change and the quit are queued and their receive thread waits until the field thread handled
    // them: queued here from another thread, which returns once the field thread ran it.
    private static Task<bool> QueueAwaited(GameSession session, FieldManager field, FieldPacketHandler handler, Action<ByteWriter> write) {
        int before = QueuedCount(field);
        Task<bool> queued = Task.Run(() => handler.TryHandleDeferred(session, TestGameSession.Packet(write)));
        SpinWait.SpinUntil(() => QueuedCount(field) > before || queued.IsCompleted, TimeSpan.FromSeconds(10));
        Assert.That(QueuedCount(field), Is.EqualTo(before + 1));
        return queued;
    }

    private static void Ends(Task<bool> queued) {
        Assert.That(queued.Wait(TimeSpan.FromSeconds(10)), Is.True);
        Assert.That(queued.Result, Is.True);
    }

    // The field thread's run of the queued packets (FieldManager.Update runs it first).
    private static void ProcessPackets(FieldManager field) {
        FieldInfo queued = typeof(FieldManager).GetField("queuedPackets", Any)!;
        if (queued.GetValue(field) == null) {
            queued.SetValue(field, Activator.CreateInstance(queued.FieldType));
        }
        typeof(FieldManager).GetMethod("ProcessPackets", Any)!.Invoke(field, null);
    }

    private static int QueuedCount(FieldManager field) =>
        ((ICollection) typeof(FieldManager).GetField("queuedPackets", Any)!.GetValue(field)!).Count;

    // The change is queued on the field thread, and its receive thread reads on only once the field thread ran it.
    [Test]
    public void ChannelChange_RunsOnTheFieldThread_AndTheReceiveThreadWaits() {
        var test = new TestGameSession();
        GameSession session = Unsaved(test);
        FieldManager field = session.Field!;
        ProcessPackets(field);

        Task<bool> queued = QueueAwaited(session, field, new ChannelHandler { World = test.World }, writer => writer.WriteShort(1));
        Assert.Multiple(() => {
            Assert.That(queued.Wait(200), Is.False);
            Assert.That(test.World.Moves, Is.Empty);
        });
        ProcessPackets(field);
        Ends(queued);
        Assert.That(test.World.Moves, Has.Count.EqualTo(1));
    }

    // The quit the same way.
    [Test]
    public void Quit_RunsOnTheFieldThread_AndTheReceiveThreadWaits() {
        var test = new TestGameSession();
        GameSession session = Unsaved(test);
        FieldManager field = session.Field!;
        ProcessPackets(field);

        Task<bool> queued = QueueAwaited(session, field, new QuitHandler { World = test.World }, writer => writer.WriteBool(false));
        Assert.Multiple(() => {
            Assert.That(queued.Wait(200), Is.False);
            Assert.That(session.MigrationSaved, Is.False);
        });
        ProcessPackets(field);
        Ends(queued);
        Assert.That(session.MigrationSaved, Is.True);
    }

    // A change queued on a field that is then disposed (a room whose time ran out) is never run there: the receive
    // thread reads on at once, not after the wait's cap.
    [Test]
    public void QueuedOnADisposedField_TheReceiveThreadReadsOn() {
        var test = new TestGameSession();
        GameSession session = Unsaved(test);
        FieldManager field = session.Field!;
        ProcessPackets(field);

        Task<bool> queued = QueueAwaited(session, field, new ChannelHandler { World = test.World }, writer => writer.WriteShort(1));
        Assert.That(queued.Wait(200), Is.False);
        typeof(FieldManager).GetProperty("Disposed", Any)!.SetValue(field, true);
        Ends(queued);
        Assert.That(test.World.Moves, Is.Empty);
    }

    // A claim sent right after the channel change is not handled: the change's save is made.
    [Test]
    public void AfterTheMoveSave_LaterPacketsAreDropped() {
        var test = new TestGameSession();
        GameSession session = Unsaved(test);
        FieldManager field = session.Field!;
        ProcessPackets(field);
        var before = new Recorder();
        var after = new Recorder();

        Queue(session, before, _ => { });
        Task<bool> change = QueueAwaited(session, field, new ChannelHandler { World = test.World }, writer => writer.WriteShort(1));
        Queue(session, after, _ => { });
        ProcessPackets(field);
        Ends(change);

        Assert.Multiple(() => {
            Assert.That(before.Handled, Is.EqualTo(1));
            Assert.That(test.World.Moves, Has.Count.EqualTo(1));
            Assert.That(session.MigrationSaved, Is.True);
            Assert.That(after.Handled, Is.EqualTo(0));
        });
    }

    // The quit to the character select the same way.
    [Test]
    public void AfterTheQuitSave_LaterPacketsAreDropped() {
        var test = new TestGameSession();
        GameSession session = Unsaved(test);
        FieldManager field = session.Field!;
        ProcessPackets(field);
        var after = new Recorder();

        Task<bool> quit = QueueAwaited(session, field, new QuitHandler { World = test.World }, writer => writer.WriteBool(false));
        Queue(session, after, _ => { });
        ProcessPackets(field);
        Ends(quit);

        Assert.Multiple(() => {
            Assert.That(session.MigrationSaved, Is.True);
            Assert.That(after.Handled, Is.EqualTo(0));
        });
    }

    // A handler that runs on the receive thread is not run after the move's save either (a guild fund donation would
    // take mesos the save does not hold); after a refused move it runs again.
    [Test]
    public void AfterTheMoveSave_ReceiveThreadPacketsAreDropped() {
        var test = new TestGameSession();
        GameSession session = Unsaved(test);
        FieldManager field = session.Field!;
        ProcessPackets(field);
        var inline = new InlineRecorder();
        var router = new PacketRouter<GameSession>(new PacketHandler<GameSession>[] { inline });
        void Send() => router.OnPacket(session, TestGameSession.Packet(writer => writer.WriteShort((short) RecvOp.Guild)));

        Send();
        Task<bool> change = QueueAwaited(session, field, new ChannelHandler { World = test.World }, writer => writer.WriteShort(1));
        ProcessPackets(field);
        Ends(change);
        Send();
        Assert.That(inline.Handled, Is.EqualTo(1));

        typeof(GameSession).GetMethod("MigrationRefused", Any)!.Invoke(session, null);
        Send();
        Assert.That(inline.Handled, Is.EqualTo(2));
    }

    // A refused change: the player plays on here, and the next packets are handled (the session saves again later).
    [Test]
    public void RefusedMove_LaterPacketsRun() {
        var test = new TestGameSession();
        GameSession session = Unsaved(test);
        FieldManager field = session.Field!;
        ProcessPackets(field);
        test.World.MoveError = new RpcException(new Status(StatusCode.Unavailable, "refused"));
        var after = new Recorder();

        Task<bool> change = QueueAwaited(session, field, new ChannelHandler { World = test.World }, writer => writer.WriteShort(1));
        Queue(session, after, _ => { });
        ProcessPackets(field);
        Ends(change);

        Assert.Multiple(() => {
            Assert.That(test.World.Moves, Has.Count.EqualTo(1));
            Assert.That(session.MigrationSaved, Is.False);
            Assert.That(after.Handled, Is.EqualTo(1));
        });
    }
}
