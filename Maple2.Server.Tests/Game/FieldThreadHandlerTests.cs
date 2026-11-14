using System;
using System.Collections;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Maple2.Model.Enum;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game;

// The handlers that used to run on the network receive thread and change what the field thread's handlers change (the
// inventory, the wallet, quests): they run on the field thread. Those that can move the player (the move saves first)
// are queued and their receive thread waits for them, as the channel change does.
public class FieldThreadHandlerTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    // The test session starts as saved and closed (TestGameSession.Start); this one plays on as a live session does.
    private static GameSession Live(TestGameSession test) {
        GameSession session = test.Start();
        typeof(GameSession).GetField("preMigrationSaved", Any)!.SetValue(session, false);
        session.State = SessionState.Connected;
        return session;
    }

    private static void ProcessPackets(FieldManager field) {
        FieldInfo queued = typeof(FieldManager).GetField("queuedPackets", Any)!;
        if (queued.GetValue(field) == null) {
            queued.SetValue(field, Activator.CreateInstance(queued.FieldType));
        }
        typeof(FieldManager).GetMethod("ProcessPackets", Any)!.Invoke(field, null);
    }

    private static int QueuedCount(FieldManager field) =>
        ((ICollection) typeof(FieldManager).GetField("queuedPackets", Any)!.GetValue(field)!).Count;

    // An unknown command: each handler ignores it, so only where it runs is tested.
    private static ByteReader Unknown() => TestGameSession.Packet(writer => writer.WriteByte(0xEE));

    private static FieldPacketHandler[] Plain() => [new LimitBreakHandler(), new SmartPushHandler()];

    private static FieldPacketHandler[] Moving(TestGameSession test) => [
        new GuildHandler { World = test.World, TableMetadata = null!, BanWordStorage = null!, ServerTableMetadata = test.ServerTableMetadata },
        new HomeHandler { World = test.World, MapMetadataStorage = null! },
        new MoveFieldHandler(),
        new DungeonRoomHandler { TableMetadata = test.TableMetadata, World = test.World },
    ];

    [Test]
    public void LimitBreakAndSmartPush_AreQueuedOnTheField() {
        var test = new TestGameSession();
        GameSession session = Live(test);
        FieldManager field = session.Field!;
        ProcessPackets(field);

        foreach (FieldPacketHandler handler in Plain()) {
            int before = QueuedCount(field);
            Assert.That(handler.TryHandleDeferred(session, Unknown()), Is.True, handler.GetType().Name);
            Assert.That(QueuedCount(field), Is.EqualTo(before + 1), handler.GetType().Name);
        }
    }

    [Test]
    public void GuildHomeMoveFieldAndDungeonRoom_AreQueued_AndTheReceiveThreadWaits() {
        var test = new TestGameSession();
        GameSession session = Live(test);
        FieldManager field = session.Field!;
        ProcessPackets(field);

        foreach (FieldPacketHandler handler in Moving(test)) {
            string name = handler.GetType().Name;
            int before = QueuedCount(field);
            Task<bool> queued = Task.Run(() => handler.TryHandleDeferred(session, Unknown()));
            SpinWait.SpinUntil(() => QueuedCount(field) > before || queued.IsCompleted, TimeSpan.FromSeconds(10));
            Assert.That(QueuedCount(field), Is.EqualTo(before + 1), name);
            Assert.That(queued.Wait(200), Is.False, name);
            ProcessPackets(field);
            Assert.That(queued.Wait(TimeSpan.FromSeconds(10)), Is.True, name);
            Assert.That(queued.Result, Is.True, name);
        }
    }

    // A queued handler that sets the session's new field (a portal, ReturnField) and then is held until let go.
    private sealed class MovingHandler : FieldPacketHandler {
        public override RecvOp OpCode => RecvOp.RequestMoveField;
        public readonly ManualResetEventSlim Moved = new();
        public readonly ManualResetEventSlim Release = new();
        public Action<GameSession>? Move;
        public override bool TryHandleDeferred(GameSession session, IByteReader reader) => QueueAndWait(session, reader);
        public override void Handle(GameSession session, IByteReader packet) {
            Move?.Invoke(session);
            Moved.Set();
            Release.Wait(TimeSpan.FromSeconds(10));
        }
    }

    // The new field is set in the middle of the move (PrepareFieldInternal): the receive thread still waits until the
    // packet is done, so the next packet does not run on the new field while the player is being moved over.
    [Test]
    public void FieldSetByTheQueuedPacket_TheReceiveThreadWaitsUntilItIsDone() {
        var test = new TestGameSession();
        GameSession session = Live(test);
        FieldManager field = session.Field!;
        ProcessPackets(field);
        var handler = new MovingHandler { Move = _ => test.EnterMap(TestGameSession.Map(2000023)) };

        Task<bool> queued = Task.Run(() => handler.TryHandleDeferred(session, Unknown()));
        SpinWait.SpinUntil(() => QueuedCount(field) > 0 || queued.IsCompleted, TimeSpan.FromSeconds(10));
        Task run = Task.Run(() => ProcessPackets(field));
        Assert.That(handler.Moved.Wait(TimeSpan.FromSeconds(10)), Is.True);
        Assert.Multiple(() => {
            Assert.That(session.Field, Is.Not.SameAs(field));
            Assert.That(queued.Wait(300), Is.False, "the wait goes on while the packet runs");
        });

        handler.Release.Set();
        Assert.That(run.Wait(TimeSpan.FromSeconds(10)), Is.True);
        Assert.That(queued.Wait(TimeSpan.FromSeconds(10)), Is.True);
        Assert.That(queued.Result, Is.True);
    }

    // A session that left the field before its packet ran: that field never runs it, and the wait ends.
    [Test]
    public void FieldLeftBeforeThePacketRan_TheWaitEnds() {
        var test = new TestGameSession();
        GameSession session = Live(test);
        FieldManager field = session.Field!;
        ProcessPackets(field);
        var handler = new MovingHandler();
        handler.Release.Set();

        Task<bool> queued = Task.Run(() => handler.TryHandleDeferred(session, Unknown()));
        SpinWait.SpinUntil(() => QueuedCount(field) > 0 || queued.IsCompleted, TimeSpan.FromSeconds(10));
        Assert.That(queued.Wait(200), Is.False);
        test.EnterMap(TestGameSession.Map(2000023));

        Assert.That(queued.Wait(TimeSpan.FromSeconds(10)), Is.True);
        Assert.That(handler.Moved.IsSet, Is.False);
    }

    // Without a field (between fields) the packet is handled on the receive thread, as before.
    [Test]
    public void NoField_HandledInline() {
        var test = new TestGameSession();
        GameSession session = test.Start();
        session.Field = null;

        FieldPacketHandler[] all = [.. Plain(), .. Moving(test)];
        foreach (FieldPacketHandler handler in all) {
            Assert.That(handler.TryHandleDeferred(session, Unknown()), Is.False, handler.GetType().Name);
        }
    }
}
