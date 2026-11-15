using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.PacketHandlers;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// StateFallDamage carries the height from where the client's fall began to where it landed (pcbase.cpp:4350-4373), both
// positions it reported in its sync records. The server logs a height above the z range of the records since the last
// one (FallHeightWatch); the damage is taken as before.
public class FallHeightLogTests {
    private TestGameSession test = null!;
    private readonly RideSyncHandler ride = new();
    private readonly FallDamageHandler fall = new();
    private readonly List<string> lines = [];

    [SetUp]
    public void Start() {
        lines.Clear();
        ILogger logger = new LoggerConfiguration().MinimumLevel.Information().WriteTo.Sink(new Lines(lines)).CreateLogger();
        FieldInfo loggerField = typeof(PacketHandler<GameSession>).GetField("Logger", BindingFlags.Instance | BindingFlags.NonPublic)!;
        loggerField.SetValue(ride, logger);
        loggerField.SetValue(fall, logger);

        test = new TestGameSession();
        test.Start();
        test.Session.State = SessionState.Connected;
        test.Session.Player.Position = Vector3.Zero;
        typeof(Actor<Player>).GetField("<Field>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(test.Session.Player, test.Session.Field);
        FieldInfo tracking = typeof(FieldPlayer).GetField("stateSyncDistanceTracking", BindingFlags.Instance | BindingFlags.NonPublic)!;
        tracking.SetValue(test.Session.Player, Activator.CreateInstance(tracking.FieldType));
        typeof(ConfigManager).GetField(nameof(ConfigManager.MoveLag))!.SetValue(test.Session.Config, new LagBudget());
        typeof(ConfigManager).GetField(nameof(ConfigManager.SyncClock))!.SetValue(test.Session.Config, new ClockRate(ClockRate.SyncBoundMs));
        test.Session.Ride = new RideManager(test.Session);
        int objectId = test.Session.Player.ObjectId;
        var metadata = new RideMetadata(1, "", new RideMetadataBasic(RideOnType.Default, 0, 0, 0, false, 0, 1), new RideMetadataSpeed(5.25f, 8f, 10f, 3.5f),
            new Dictionary<BasicAttribute, long>());
        test.Session.Ride.Ride = new Ride(objectId, metadata, new RideOnAction(1, objectId));
        test.TakeSent();
    }

    // One walk record at (0, 0, z) (commonfiles/statesyncobject.cpp:113-145, 222), as RideSpeedTests sends it.
    private void Sync(short z, int tick) {
        try {
            ride.Handle(test.Session, TestGameSession.Packet(writer => {
                writer.WriteByte(0);
                writer.WriteInt(1000);
                writer.WriteInt(2000);
                writer.WriteByte(1);
                writer.WriteByte((byte) ActorState.Walk);
                writer.WriteByte(0);
                writer.WriteShort(0);
                writer.WriteShort(0);
                writer.WriteShort(z);
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
            // The test field has no bounds for the move itself; the watch runs before it.
        }
    }

    private void Fall(float height) {
        try {
            fall.Handle(test.Session, TestGameSession.Packet(writer => writer.WriteFloat(height)));
        } catch (NullReferenceException e) when (e.StackTrace?.Contains("FallDamage") == true && !e.StackTrace.Contains("FallHeightWatch")) {
            // The test field has no metadata limits; the watch runs before the damage.
        }
    }

    private List<string> Above() => lines.Where(line => line.Contains("above the records")).ToList();

    // A second report settles the first at once, with no grace to wait.
    [Test]
    public void AFallAboveTheRideRecords_IsLogged() {
        Sync(1000, 1000);
        Sync(0, 1500);
        Fall(5000);
        Assert.That(Above(), Is.Empty, "pending its grace");

        Fall(0);

        Assert.That(Above(), Has.Count.EqualTo(1));
    }

    [Test]
    public void AFallWithinTheRideRecords_IsNotLogged() {
        Sync(3000, 1000);
        Sync(0, 1500);
        Fall(2900);
        Fall(0);

        Assert.That(Above(), Is.Empty);
    }

    private sealed class Lines(List<string> lines) : ILogEventSink {
        public void Emit(LogEvent logEvent) => lines.Add(logEvent.RenderMessage());
    }
}
