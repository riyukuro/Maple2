using System.Numerics;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// RideSync: the client sends it only from its mount's controller (StateRideeController, state.cpp:536), and the other
// clients drop one for a player without a mount (scenestategame.cpp:2478). The server took it from anyone: it moved
// the player and set its state to Ride. (A rider's sync reaches the field's bounds check, which the test field lacks.)
public class RideSyncRiderTests {
    private static TestGameSession Start() {
        var test = new TestGameSession();
        test.Start();
        test.Session.State = SessionState.Connected;
        test.Session.Player.Position = Vector3.Zero;
        // The test player is made without its field.
        typeof(Actor<Player>).GetField("<Field>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(test.Session.Player, test.Session.Field);
        FieldInfo tracking = typeof(FieldPlayer).GetField("stateSyncDistanceTracking", BindingFlags.Instance | BindingFlags.NonPublic)!;
        tracking.SetValue(test.Session.Player, System.Activator.CreateInstance(tracking.FieldType));
        test.Session.Ride = new RideManager(test.Session);
        test.TakeSent();
        return test;
    }

    // One walk record at (100, 200, 300) in the client's order (commonfiles/statesyncobject.cpp:113-145, 222).
    private static void Sync(TestGameSession test) {
        new RideSyncHandler().Handle(test.Session, TestGameSession.Packet(writer => {
            writer.WriteByte(0);
            writer.WriteInt(1000);
            writer.WriteInt(2000);
            writer.WriteByte(1);
            writer.WriteByte((byte) ActorState.Walk);
            writer.WriteByte(0);
            writer.WriteShort(100);
            writer.WriteShort(200);
            writer.WriteShort(300);
            writer.WriteShort(900);
            writer.WriteByte(9);
            writer.WriteShort(0);
            writer.WriteShort(0);
            writer.WriteShort(0);
            writer.WriteByte(2);
            writer.WriteShort(0);
            writer.WriteShort(0);
            writer.WriteByte(0);
            writer.WriteInt(0);
            writer.WriteInt(111);
        }));
    }

    [Test]
    public void ASyncWithNoMount_IsDropped() {
        TestGameSession test = Start();
        Sync(test);
        Assert.Multiple(() => {
            Assert.That(test.Session.Player.Position, Is.EqualTo(Vector3.Zero));
            Assert.That(test.Session.Player.State, Is.Not.EqualTo(ActorState.Ride));
        });
    }
}
