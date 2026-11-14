using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// RideSync positions were never held to any speed: a mount's client could put the player anywhere. The client moves a
// mount at the ride's speed times the riding rates and the move speed rate (RideSpeed); a move beyond that, with
// slack, is now counted and logged as UserSync's are.
public class RideSpeedTests {
    private static TestGameSession Start(RideMetadataSpeed speed) {
        var test = new TestGameSession();
        test.Start();
        test.Session.State = SessionState.Connected;
        test.Session.Player.Position = Vector3.Zero;
        typeof(Actor<Player>).GetField("<Field>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(test.Session.Player, test.Session.Field);
        FieldInfo tracking = typeof(FieldPlayer).GetField("stateSyncDistanceTracking", BindingFlags.Instance | BindingFlags.NonPublic)!;
        tracking.SetValue(test.Session.Player, System.Activator.CreateInstance(tracking.FieldType));
        // The test config is made without its sync clocks.
        typeof(ConfigManager).GetField(nameof(ConfigManager.MoveLag))!.SetValue(test.Session.Config, new LagBudget());
        typeof(ConfigManager).GetField(nameof(ConfigManager.SyncClock))!.SetValue(test.Session.Config, new ClockRate(ClockRate.SyncBoundMs));
        test.Session.Ride = new RideManager(test.Session);
        int objectId = test.Session.Player.ObjectId;
        var metadata = new RideMetadata(1, "", new RideMetadataBasic(RideOnType.Default, 0, 0, 0, false, 0, 1), speed,
            new Dictionary<BasicAttribute, long>());
        test.Session.Ride.Ride = new Ride(objectId, metadata, new RideOnAction(1, objectId));
        test.TakeSent();
        return test;
    }

    // One walk record at (x, 0, 0) with the client's tick (commonfiles/statesyncobject.cpp:113-145, 222).
    private static void Sync(TestGameSession test, short x, int tick) {
        try {
            new RideSyncHandler().Handle(test.Session, TestGameSession.Packet(writer => {
                writer.WriteByte(0);
                writer.WriteInt(1000);
                writer.WriteInt(2000);
                writer.WriteByte(1);
                writer.WriteByte((byte) ActorState.Walk);
                writer.WriteByte(0);
                writer.WriteShort(x);
                writer.WriteShort(0);
                writer.WriteShort(0);
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
                writer.WriteInt(tick);
            }));
        } catch (NullReferenceException e) when (e.StackTrace?.Contains("OnStateSync") == true) {
            // The test field has no bounds for the move itself; the check runs before it.
        }
    }

    // KR's fastest ride (run 8, dash 10): 10 * 1.25 * 1.7 * 100 = 2125 a second, so 2806 in the window with slack (x 1.25 + 150).
    [TestCase(2600, 0)]
    [TestCase(3000, 1)]
    public void AMoveBeyondTheRidesSpeed_IsCounted(int distance, int fast) {
        TestGameSession test = Start(new RideMetadataSpeed(5.25f, 8f, 10f, 3.5f));
        Sync(test, 0, 1000);
        Sync(test, (short) distance, 1500);
        Assert.That(test.Session.Player.RideMoves.Fast, Is.EqualTo(fast));
    }

    [Test]
    public void TheBound_IsNeverBelowAWalks() {
        Assert.That(RideSpeed.MaxDistance(new RideMetadataSpeed(0, 0, 0, 0), 0, 0), Is.EqualTo(MoveRules.MaxDistance));
    }

    [Test]
    public void AFasterBuffThanKrs_RaisesTheBound() {
        var speed = new RideMetadataSpeed(5.25f, 8f, 10f, 3.5f);
        Assert.That(RideSpeed.MaxDistance(speed, 0.5, 0), Is.GreaterThan(RideSpeed.MaxDistance(speed, 0, 0)));
    }
}
