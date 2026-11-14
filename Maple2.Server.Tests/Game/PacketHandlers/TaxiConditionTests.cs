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
// <condition type="taxiuse" value="1" />. The ride reaches PrepareField, which stops at the test field (one the
// played client lacks), as in FreeAirTaxiTests. The field ids are made up.
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

    private static MapMetadata Field(int id, bool notInPlayedClient = false) {
        MapMetadata map = TestGameSession.Map(id, notInPlayedClient: notInPlayedClient);
        return map with {
            XBlock = XBlock,
            CashCall = new MapMetadataCashCall(true, true, false, false, false),
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

    private static (long Counter, long Paid) Ride(long meso) {
        var test = new TestGameSession();
        MapMetadata from = Field(From);
        test.AddMap(from, Field(To, notInPlayedClient: true));
        test.SetTable(new FieldMetaTable(new Dictionary<int, IReadOnlyList<int>> { [From] = [To] }, new Dictionary<int, IReadOnlyList<int>>()));
        GameSession session = test.Start();
        test.EnterMap(from);
        // The client rides only from its field's station: the player stands on it.
        typeof(FieldManager).GetField("<Entities>k__BackingField", Any)!.SetValue(session.Field, Station());
        session.Player.Position = Vector3.Zero;
        session.Player.Value.Character.MapId = From;
        session.Player.Value.Unlock.Taxis.Add(To);
        session.Currency.Meso = meso;
        Quest quest = test.StartQuest(TestGameSession.QuestData(91000001) with {
            Conditions = [new ConditionMetadata(ConditionType.taxiuse, 1, null, null)],
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

    [Test]
    public void ARideRefusedForTheFareDoesNot() {
        (long counter, long paid) = Ride(0);
        Assert.Multiple(() => {
            Assert.That(paid, Is.Zero);
            Assert.That(counter, Is.Zero);
        });
    }
}
