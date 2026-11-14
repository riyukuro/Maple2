using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Maple2.Tools.Scheduler;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// PlayInstrument in a field of two players: the fireworks (14) of the concert dialog reach the field, and a player
// who leaves the field while playing takes the instrument along (the field's list is sent to later arrivals).
public class InstrumentFieldTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int InstrumentItemId = 34000003;
    private const int ScoreItemId = 35000033;
    private const int InstrumentId = 1;
    private const int MapId = 2000001;

    private TestGameSession playerTest = null!;
    private TestGameSession watcherTest = null!;
    private GameSession player => playerTest.Session;

    // The player and a watcher in the player's field.
    [SetUp]
    public void SetUp() {
        DbContextOptions options = new DbContextOptionsBuilder()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        playerTest = new TestGameSession(options);
        watcherTest = new TestGameSession(options);
        foreach (TestGameSession test in new[] { playerTest, watcherTest }) {
            test.Store(TestGameSession.Metadata(InstrumentItemId, type: 6,
                function: new ItemMetadataFunction(ItemFunction.OpenInstrument, "OpenInstrument", $"{InstrumentId}", false)));
            test.Store(TestGameSession.Metadata(ScoreItemId, type: 6) with {
                Music = new ItemMetadataMusic(10, 1, 1, false, 0, string.Empty, 60000),
            });
            test.SetTable(new InstrumentTable(new Dictionary<int, InstrumentMetadata> {
                [InstrumentId] = new(InstrumentId, 0, 1, 1, 1, 0),
            }));
            test.Start();
            test.EnterMap(TestGameSession.Map(MapId));
        }
        // Each test session's player has object id 1: the watcher takes another.
        typeof(Maple2.Server.Game.Model.Actor<Player>).GetField("<ObjectId>k__BackingField", Any)!.SetValue(watcherTest.Session.Player, 2);
        watcherTest.Session.Field = player.Field;
        player.Field!.Players[watcherTest.Session.Player.ObjectId] = watcherTest.Session.Player;
        playerTest.TakeSent();
        watcherTest.TakeSent();
    }

    private (long Instrument, long Score) Items() {
        Item instrument = player.Item.Inventory.Filter(item => item.Id == InstrumentItemId, InventoryType.FishingMusic).Single();
        Item score = player.Item.Inventory.Filter(item => item.Id == ScoreItemId, InventoryType.FishingMusic).Single();
        instrument.ExpiryTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 3600;
        score.RemainUses = 5;
        return (instrument.Uid, score.Uid);
    }

    private void Send(byte command, params long[] values) {
        new InstrumentHandler { TableMetadata = playerTest.TableMetadata, BanWordStorage = null! }.Handle(player, TestGameSession.Packet(writer => {
            writer.WriteByte(command);
            foreach (long value in values) {
                writer.WriteLong(value);
            }
        }));
    }

    // What a started session holds and the leave clears: the test session is made without its initializers.
    private void LeaveField() {
        foreach (string name in new[] { nameof(GameSession.InteractStarted), nameof(GameSession.CubeStarted) }) {
            typeof(GameSession).GetField(name)!.SetValue(player, new HashSet<string>());
        }
        typeof(GameSession).GetField(nameof(GameSession.ItemLockStaging))!.SetValue(player, new long[18]);
        FieldInfo dismantle = typeof(GameSession).GetField(nameof(GameSession.DismantleStaging))!;
        dismantle.SetValue(player, Array.CreateInstance(dismantle.FieldType.GetElementType()!, 0));
        typeof(GameSession).GetField(nameof(GameSession.Scheduler))!.SetValue(player, new EventQueue(Serilog.Log.Logger));
        // The field's own removal of the player is not under test: the player is taken out of its list first.
        player.Field!.Players.TryRemove(player.Player.ObjectId, out _);
        typeof(GameSession).GetMethod("LeaveField", Any)!.Invoke(player, null);
    }

    private ConcurrentDictionary<int, FieldInstrument> Instruments(FieldManager field) =>
        (ConcurrentDictionary<int, FieldInstrument>) typeof(FieldManager).GetField("fieldInstruments", Any)!.GetValue(field)!;

    // The PlayInstrument packets the watcher got: (mode, first int after it).
    private List<(byte Mode, int Id)> WatcherGot() => watcherTest.TakeSent()
        .Where(packet => TestGameSession.Op(packet) == SendOp.PlayInstrument)
        .Select(packet => (packet[2], BitConverter.ToInt32(packet, 3))).ToList();

    // UIMusicConcertDialog::SendPlayInstrumentMode14 is open to any player in the concert dialog; the client draws the
    // effect over the id it reads (kind 1 for itself, 3 for another), so every player of the field is sent it.
    [Test]
    public void Fireworks_ReachTheOtherPlayersOfTheField() {
        typeof(FieldManager).GetField(nameof(FieldManager.PerformanceStage), Any)!.SetValue(player.Field, new PerformanceStageManager(player.Field!));

        Send(14);

        Assert.That(WatcherGot(), Is.EqualTo(new List<(byte, int)> { (14, player.Player.ObjectId) }));
    }

    [Test]
    public void LeaveField_EndsTheScoreForTheOthers() {
        (long instrument, long score) = Items();
        Send(3, instrument, score);
        FieldInstrument? playing = player.Instrument;
        Assume.That(playing, Is.Not.Null, "the score plays");
        FieldManager field = player.Field!;
        Assume.That(Instruments(field).ContainsKey(playing!.ObjectId), "in the field's list");
        watcherTest.TakeSent();

        LeaveField();

        Assert.That(Instruments(field), Is.Empty);
        Assert.That(WatcherGot(), Is.EqualTo(new List<(byte, int)> { (4, playing.ObjectId) }));
        Assert.That(player.Instrument, Is.Null);
    }

    [Test]
    public void LeaveField_EndsTheImprovisationForTheOthers() {
        (long instrument, _) = Items();
        Send(0, instrument);
        FieldInstrument? playing = player.Instrument;
        Assume.That(playing?.Improvising, Is.True, "improvising");
        FieldManager field = player.Field!;
        watcherTest.TakeSent();

        LeaveField();

        Assert.That(Instruments(field), Is.Empty);
        Assert.That(WatcherGot(), Is.EqualTo(new List<(byte, int)> { (2, playing!.ObjectId) }));
    }

    // A member waiting for the leader's ensemble start (5) stops waiting with the field it leaves.
    [Test]
    public void LeaveField_ClearsTheEnsembleReadyMark() {
        player.EnsembleReady = true;
        player.StagedScoreItem = player.Item.Inventory.Filter(item => item.Id == ScoreItemId, InventoryType.FishingMusic).Single();

        LeaveField();

        Assert.That((player.EnsembleReady, player.StagedInstrumentItem, player.StagedScoreItem), Is.EqualTo((false, (Item?) null, (Item?) null)));
    }
}
