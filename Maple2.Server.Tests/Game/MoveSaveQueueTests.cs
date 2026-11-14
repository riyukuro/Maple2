using System;
using System.Collections;
using System.Reflection;
using Grpc.Core;
using Maple2.Model.Enum;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
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

    [Test]
    public void ChannelAndQuit_RunOnTheFieldThread() {
        var test = new TestGameSession();
        GameSession session = Unsaved(test);
        FieldManager field = session.Field!;
        ProcessPackets(field);

        Queue(session, new ChannelHandler { World = test.World }, writer => writer.WriteShort(1));
        Queue(session, new QuitHandler { World = test.World }, writer => writer.WriteBool(false));
        Assert.Multiple(() => {
            Assert.That(QueuedCount(field), Is.EqualTo(2));
            Assert.That(test.World.Moves, Is.Empty);
            Assert.That(session.State, Is.EqualTo(SessionState.Connected));
        });
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
        Queue(session, new ChannelHandler { World = test.World }, writer => writer.WriteShort(1));
        Queue(session, after, _ => { });
        ProcessPackets(field);

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

        Queue(session, new QuitHandler { World = test.World }, writer => writer.WriteBool(false));
        Queue(session, after, _ => { });
        ProcessPackets(field);

        Assert.Multiple(() => {
            Assert.That(session.MigrationSaved, Is.True);
            Assert.That(after.Handled, Is.EqualTo(0));
        });
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

        Queue(session, new ChannelHandler { World = test.World }, writer => writer.WriteShort(1));
        Queue(session, after, _ => { });
        ProcessPackets(field);

        Assert.Multiple(() => {
            Assert.That(test.World.Moves, Has.Count.EqualTo(1));
            Assert.That(session.MigrationSaved, Is.False);
            Assert.That(after.Handled, Is.EqualTo(1));
        });
    }
}
