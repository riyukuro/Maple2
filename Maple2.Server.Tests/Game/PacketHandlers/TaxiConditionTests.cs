using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Caching;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// RequestTaxi mode 1 (the ground taxi) counts taxiuse once its fare is paid. 70400012:
// <condition type="taxiuse" value="1" />. The ride leaves as a move to another server (TestGameSession.EnterMovingOut)
// unless a test refuses it, as in FreeAirTaxiTests. The air taxi's cash-call flags are not the ground taxi's: the client's
// TakeTaxi (uiworldmapdialog.cpp:1369) asks none of them, so a ride between fields without them goes too. The field ids
// are made up.
public class TaxiConditionTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int From = 2000913, To = 2000914;
    // A name of its own per run: the navmesh file sits in the shared build output, where another test run may delete it.
    private static readonly string XBlock = $"taxi_condition_test_{Guid.NewGuid():N}";
    private static string MeshPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Navmeshes", $"{XBlock}.navmesh");
    private bool madeMesh;

    [SetUp]
    public void MakeMesh() {
        // CheckMapCashCall refuses a field without a navmesh file; only the file's presence is asked.
        Directory.CreateDirectory(Path.GetDirectoryName(MeshPath)!);
        if (!System.IO.File.Exists(MeshPath)) {
            System.IO.File.WriteAllBytes(MeshPath, []);
            madeMesh = true;
        }
    }

    [TearDown]
    public void RemoveMesh() {
        if (madeMesh) {
            System.IO.File.Delete(MeshPath);
            madeMesh = false;
        }
    }

    private static MapMetadata Field(int id, bool notInPlayedClient = false, bool airTaxi = true) {
        MapMetadata map = TestGameSession.Map(id, notInPlayedClient: notInPlayedClient);
        return map with {
            XBlock = XBlock,
            CashCall = new MapMetadataCashCall(airTaxi, airTaxi, false, false, false),
            Limit = new MapMetadataLimit(0, 0, 0, 0, [], false, false, false, false, false, false, false),
        };
    }

    // A field's entities with a taxi station at the origin.
    private static MapEntityMetadata Station() {
        var entities = (MapEntityMetadata) RuntimeHelpers.GetUninitializedObject(typeof(MapEntityMetadata));
        typeof(MapEntityMetadata).GetField("<Taxi>k__BackingField", Any)!.SetValue(entities, new TaxiStation(Vector3.Zero, Vector3.Zero));
        return entities;
    }

    // The taxi stop's entities: a station, which the ride needs.
    private static MapEntityStorage Entities() {
        var storage = (MapEntityStorage) RuntimeHelpers.GetUninitializedObject(typeof(MapEntityStorage));
        var cache = new LRUCache<string, MapEntityMetadata>(16, 1);
        cache.AddReplace(XBlock, Station());
        typeof(MetadataStorage<string, MapEntityMetadata>).GetField("Cache", Any)!.SetValue(storage, cache);
        return storage;
    }

    private static (long Counter, long Paid) Ride(long meso, FreeAirTaxiTests.Outcome outcome = FreeAirTaxiTests.Outcome.Went, bool airTaxi = true,
        int[]? fieldMissionMaps = null) {
        var test = new TestGameSession();
        MapMetadata from = Field(From, airTaxi: airTaxi);
        test.AddMap(from, Field(To, notInPlayedClient: outcome == FreeAirTaxiTests.Outcome.RefusedHere, airTaxi: airTaxi));
        test.SetTable(new FieldMetaTable(new Dictionary<int, IReadOnlyList<int>> { [From] = [To] }, new Dictionary<int, IReadOnlyList<int>>()));
        GameSession session = test.Start();
        if (outcome == FreeAirTaxiTests.Outcome.RefusedHere) {
            test.EnterMap(from);
        } else {
            test.EnterMovingOut(from);
            if (outcome == FreeAirTaxiTests.Outcome.WorldRefused) {
                test.World.MoveAddress = "";
            }
        }
        // The client rides only from its field's station: the player stands on it.
        typeof(FieldManager).GetField("<Entities>k__BackingField", Any)!.SetValue(session.Field, Station());
        session.Player.Position = Vector3.Zero;
        session.Player.Value.Character.MapId = From;
        session.Player.Value.Unlock.Taxis.Add(To);
        session.Currency.Meso = meso;
        QuestMetadata metadata = TestGameSession.QuestData(91000001, fieldMissionMaps == null ? QuestType.EpicQuest : QuestType.FieldMission);
        Quest quest = test.StartQuest(metadata with {
            Basic = metadata.Basic with { ProgressMaps = fieldMissionMaps },
            // A field mission completes itself at its goal: a goal of 2 keeps the count to read.
            Conditions = [new ConditionMetadata(ConditionType.taxiuse, fieldMissionMaps == null ? 1 : 2, null, null)],
        });
        new TaxiHandler {
            MapMetadata = test.MapMetadata,
            EntityMetadata = Entities(),
            TaxiRoute = new TaxiRouteStorage(test.TableMetadata, test.MapMetadata),
            ServerTableMetadata = test.ServerTableMetadata,
        }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(1);
            writer.WriteInt(To);
        }));
        return (quest.Conditions[0].Counter, meso - session.Currency.Meso);
    }

    [Test]
    public void ThePaidRideCounts() {
        (long counter, long paid) = Ride(1_000_000);
        Assert.Multiple(() => {
            Assert.That(paid, Is.GreaterThan(0));
            Assert.That(counter, Is.EqualTo(1));
        });
    }

    // A ride whose field change is refused (here, or by the World) gives the fare back and counts nothing. Before, the
    // fare was taken and the ride counted ahead of the field change.
    [TestCase(FreeAirTaxiTests.Outcome.WorldRefused)]
    [TestCase(FreeAirTaxiTests.Outcome.RefusedHere)]
    public void ARefusedRide_KeepsTheFareAndDoesNotCount(FreeAirTaxiTests.Outcome outcome) {
        (long counter, long paid) = Ride(1_000_000, outcome);
        Assert.Multiple(() => {
            Assert.That(paid, Is.Zero);
            Assert.That(counter, Is.Zero);
        });
    }

    [Test]
    public void ARideBetweenFieldsWithoutTheAirTaxiFlags_GoesAndCounts() {
        (long counter, long paid) = Ride(1_000_000, airTaxi: false);
        Assert.Multiple(() => {
            Assert.That(paid, Is.GreaterThan(0));
            Assert.That(counter, Is.EqualTo(1));
        });
    }

    // A field mission "Travel by taxi from <map>" (KR 72000065: taxiuse, progress map 02000036) counts the ride from its
    // map, not the ride to it. The ride's count runs once the change went, with the character on the arrival map; it
    // counted for the arrival's mission.
    [TestCase(From, 1)]
    [TestCase(To, 0)]
    public void AFieldMissionCountsTheRideFromItsMap(int progressMap, long expected) {
        (long counter, long paid) = Ride(1_000_000, fieldMissionMaps: [progressMap]);
        Assert.Multiple(() => {
            Assert.That(paid, Is.GreaterThan(0));
            Assert.That(counter, Is.EqualTo(expected));
        });
    }

    [Test]
    public void ARideRefusedForTheFareDoesNot() {
        (long counter, long paid) = Ride(0);
        Assert.Multiple(() => {
            Assert.That(paid, Is.Zero);
            Assert.That(counter, Is.Zero);
        });
    }

    // 22100010: <condition type="taxifind" value="1" />: the station's discovery (the action key, mode 0, or the
    // station finder, mode 5) counts once; a station already known counts nothing, and a map the server does not
    // know reveals nothing.
    [TestCase((byte) 0)]
    [TestCase((byte) 5)]
    public void TaxiFind_22100010_TheStationsDiscovery(byte mode) {
        var test = new TestGameSession();
        test.AddMap(Field(From));
        test.SetTable(new CommonExpTable(new Dictionary<ExpType, CommonExpTable.Entry>()));
        GameSession session = test.Start();
        test.EnterMap(TestGameSession.Map(To));
        QuestMetadata metadata = TestGameSession.QuestData(91000001);
        Quest quest = test.StartQuest(metadata with { Conditions = [new ConditionMetadata(ConditionType.taxifind, 2, null, null)] });
        var handler = new TaxiHandler {
            MapMetadata = test.MapMetadata,
            EntityMetadata = Entities(),
            TaxiRoute = new TaxiRouteStorage(test.TableMetadata, test.MapMetadata),
            ServerTableMetadata = test.ServerTableMetadata,
        };

        session.Player.Value.Character.MapId = To;
        handler.Handle(session, TestGameSession.Packet(writer => writer.WriteByte(mode)));
        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(0));
        session.Player.Value.Character.MapId = From;
        handler.Handle(session, TestGameSession.Packet(writer => writer.WriteByte(mode)));
        handler.Handle(session, TestGameSession.Packet(writer => writer.WriteByte(mode)));

        Assert.That(test.Errors, Is.Empty);
        Assert.That(session.Player.Value.Unlock.Taxis, Does.Contain(From).And.Not.Contain(To));
        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(1));
    }
}
