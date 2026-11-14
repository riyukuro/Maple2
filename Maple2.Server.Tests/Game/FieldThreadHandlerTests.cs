using System;
using System.Collections;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Maple2.Model.Enum;
using Maple2.PacketLib.Tools;
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
