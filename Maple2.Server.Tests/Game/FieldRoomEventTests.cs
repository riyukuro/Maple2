using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game;

// timeEventData type 2: the random room portals a field opens at its tagged regions. KR row 10000 opens on map
// 02000014, every 7 minutes (late by up to 6:30) at prob 80, one portal for 29 s at a bonus map region, to a room of
// randomroom 1; row 11001 opens the antlion holes (randomroom 2) on 02010033. Before, no server code read type 2: every
// bonus map region opened a hat at a random 5 minutes to 8 hours, and the antlion regions none.
public class FieldRoomEventTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int MapId = 2000014;
    private const string BonusTag = "\uBCF4\uB108\uC2A4\uB9F5";
    private const string AntlionTag = "\uAC1C\uBBF8\uC9C0\uC625";
    private const int BonusPoint = 3000;
    private const int AntlionPoint = 3001;
    private const int OtherPoint = 3002;

    private static readonly DateTime Start = new(2018, 1, 1);

    private static FieldRoomEventMetadata Row(int id = 10000, int probability = 100, string tag = BonusTag, int randomRoom = 1,
        TimeSpan? life = null, int count = 1) =>
        new(id, probability, Start, new DateTime(2099, 1, 1), TimeSpan.FromMinutes(7), TimeSpan.Zero, life ?? TimeSpan.FromSeconds(29),
            [MapId], [tag], randomRoom, count);

    private static MapMetadataSpawn Spawn(int id, params string[] tags) => new(id, 0, 0, 0, 0, tags, 0, 0, new Dictionary<int, int>());

    private TestGameSession test = null!;

    private FieldManager Field(params FieldRoomEventMetadata[] rows) => Field(null, rows);

    private FieldManager Field(InstanceType? instance, params FieldRoomEventMetadata[] rows) {
        test = new TestGameSession();
        test.Start();
        if (instance is { } type) {
            test.SetServerTable(new InstanceFieldTable(new Dictionary<int, InstanceFieldMetadata> {
                [MapId] = new(MapId, type, 1, false, 0, false, 0, 0, 0, 0),
            }));
        }
        test.SetServerTable(new PrestigeExpTable(new Dictionary<ExpType, long>()));
        test.SetTable(new CommonExpTable(new Dictionary<ExpType, CommonExpTable.Entry>()));
        test.SetTable(new ChangeJobTable(new Dictionary<Job, ChangeJobMetadata>()));
        test.SetTable(new ChapterBookTable(new Dictionary<int, ChapterBookTable.Entry>()));
        test.SetTable(new FameTable([], new Dictionary<int, AllianceType>()));
        test.SetTable(new ExpTable(new Dictionary<int, IReadOnlyDictionary<int, long>>(), new Dictionary<int, long>()));
        // KR room_random.xml: randomroom 1 (prob 10000) to room 11 and randomroom 2 to room 201 (kept to one ref each).
        test.SetServerTable(new RoomRandomTable(new Dictionary<int, RandomRoomMetadata> {
            [1] = new(1, 10000, [new RandomRoomMetadata.Ref(11, 100)]),
            [2] = new(2, 10000, [new RandomRoomMetadata.Ref(201, 100)]),
        }, new Dictionary<int, RandomRoomFieldMetadata> {
            [11] = new(11, [80000001], 30000, "Eff_event_portal_A01", 1, true),
            [201] = new(201, [80000012], 60000, "Eff_kr_sandswirl_01", 1, true),
        }));
        test.EnterMap(TestGameSession.Map(MapId) with {
            Spawns = [Spawn(BonusPoint, BonusTag), Spawn(AntlionPoint, " " + AntlionTag), Spawn(OtherPoint, "other")],
        });
        FieldManager field = test.Session.Field!;
        test.SetServerTable(new TimeEventTable(new Dictionary<int, GlobalPortalMetadata>(), new Dictionary<int, WorldBossMetadata>(),
            FieldRoomEvent: rows.ToDictionary(row => row.Id)));
        var entities = (MapEntityMetadata) RuntimeHelpers.GetUninitializedObject(typeof(MapEntityMetadata));
        typeof(MapEntityMetadata).GetField("<RegionSpawns>k__BackingField", Any)!.SetValue(entities, new Dictionary<int, Ms2RegionSpawn> {
            [BonusPoint] = new(BonusPoint, false, new Vector3(150, 300, 450), Vector3.Zero),
            [AntlionPoint] = new(AntlionPoint, false, new Vector3(1500, 300, 450), Vector3.Zero),
            [OtherPoint] = new(OtherPoint, false, new Vector3(3000, 300, 450), Vector3.Zero),
        });
        typeof(FieldManager).GetField("<Entities>k__BackingField", Any)!.SetValue(field, entities);
        // The test field is built without its initializers: its portals and the points' portals start empty.
        typeof(FieldManager).GetField("fieldPortals", Any)!.SetValue(field, new ConcurrentDictionary<int, FieldPortal>());
        typeof(FieldManager).GetField("roomEventPortals", Any)!.SetValue(field, new Dictionary<int, FieldPortal>());
        return field;
    }

    private static List<FieldPortal> Rooms(FieldManager field) => field.GetPortals().Where(portal => portal.RandomRoom != null).ToList();

    [Test]
    public void Points_AreTheTaggedRegionsOfTheMap() {
        MapMetadataSpawn[] spawns = [Spawn(5, BonusTag), Spawn(4, "x", BonusTag + " "), Spawn(6, AntlionTag), Spawn(7, BonusTag)];
        // 7 is no region of the map.
        Assert.That(FieldRoomEventRules.Points(Row(), spawns, [4, 5, 6]), Is.EqualTo(new[] { 4, 5 }));
        Assert.That(FieldRoomEventRules.Points(Row(tag: AntlionTag), spawns, [4, 5, 6]), Is.EqualTo(new[] { 6 }));
    }

    [Test]
    public void Row10000_OpensOneBonusPortalToItsRandomRoomForWhatIsLeftOfItsLife() {
        FieldManager field = Field(Row());
        Assert.That(Rooms(field), Is.Empty);
        long tick = field.FieldTick;

        // 00:00:10 at field load: the run of 00:00 is alive 19 s more.
        field.UpdateRoomEvents(Start.AddSeconds(10));

        FieldPortal portal = Rooms(field).Single();
        Assert.Multiple(() => {
            Assert.That(portal.Position, Is.EqualTo(new Vector3(150, 300, 450)));
            Assert.That(portal.RandomRoom!.Id, Is.EqualTo(11));
            Assert.That(portal.Value.TargetMapId, Is.EqualTo(80000001));
            Assert.That(portal.Model, Is.EqualTo("Eff_event_portal_A01"));
            Assert.That(portal.EndTick - tick, Is.InRange(18000, 20000));
        });
    }

    [Test]
    public void Row11001_OpensTheAntlionHoleOnly() {
        FieldManager field = Field(Row(id: 11001, tag: AntlionTag, randomRoom: 2));
        field.UpdateRoomEvents(Start.AddSeconds(1));

        FieldPortal portal = Rooms(field).Single();
        Assert.Multiple(() => {
            Assert.That(portal.Position, Is.EqualTo(new Vector3(1500, 300, 450)));
            Assert.That(portal.RandomRoom!.Id, Is.EqualTo(201));
            Assert.That(portal.Model, Is.EqualTo("Eff_kr_sandswirl_01"));
        });
    }

    [Test]
    public void NoPortalOutsideTheLifeOrWithoutProb() {
        FieldManager field = Field(Row(), Row(id: 10001, probability: 0, tag: AntlionTag, randomRoom: 2));
        // 00:01: the run of 00:00 ended at 00:00:29.
        field.UpdateRoomEvents(Start.AddMinutes(1));
        Assert.That(Rooms(field), Is.Empty);
        // 00:07: the next run of row 10000; the prob 0 row never opens.
        field.UpdateRoomEvents(Start.AddMinutes(7));
        Assert.That(Rooms(field).Select(portal => portal.RandomRoom!.Id), Is.EqualTo(new[] { 11 }));
    }

    [Test]
    public void AnOccupiedPointGetsNoSecondPortal() {
        // A life longer than the cycle: the portal of 00:00 is still open at 00:07.
        FieldManager field = Field(Row(life: TimeSpan.FromMinutes(10)));
        field.UpdateRoomEvents(Start.AddSeconds(1));
        FieldPortal first = Rooms(field).Single();

        field.UpdateRoomEvents(Start.AddMinutes(7));
        Assert.That(Rooms(field), Is.EqualTo(new[] { first }));

        // Once it is gone the point is free at the next run.
        field.RemovePortal(first.ObjectId);
        field.UpdateRoomEvents(Start.AddMinutes(14));
        FieldPortal next = Rooms(field).Single();
        Assert.That(next, Is.Not.SameAs(first));
    }

    // rv475 #1: a field made again in a run its map placed already (the old one disposed when empty) opens nothing
    // until the next run.
    [Test]
    public void AFieldMadeAgainInTheRunDoesNotOpenItAgain() {
        FieldManager first = Field(Row());
        first.UpdateRoomEvents(Start.AddSeconds(10));
        Assert.That(Rooms(first), Has.Count.EqualTo(1));
        var placed = first.InteractEventRunsPlaced;

        FieldManager second = Field(Row());
        second.InteractEventRunsPlaced = placed;
        second.UpdateRoomEvents(Start.AddSeconds(15));
        Assert.That(Rooms(second), Is.Empty);
        second.UpdateRoomEvents(Start.AddMinutes(7));
        Assert.That(Rooms(second), Has.Count.EqualTo(1));
    }

    // rv475 #4: with randomTime the run starts late (here 3 min after the 00:07 grid run, which ended at 00:07:29) and
    // lives lifeTime from that start: updated at 00:10:05 it opens for the 24 s left.
    [Test]
    public void ALateRunLivesFromItsOwnStart() {
        FieldManager field = Field(Row() with { RandomTime = TimeSpan.FromMinutes(6.5) });
        field.UpdateRoomEvents(Start.AddMinutes(1));
        Assert.That(Rooms(field), Is.Empty);
        object state = ((System.Collections.IList) typeof(FieldManager).GetField("roomEvents", Any)!.GetValue(field)!)[0]!;
        state.GetType().GetField("NextRun")!.SetValue(state, Start.AddMinutes(10));
        long tick = field.FieldTick;

        field.UpdateRoomEvents(Start.AddMinutes(10).AddSeconds(5));
        FieldPortal portal = Rooms(field).Single();
        Assert.That(portal.EndTick - tick, Is.InRange(23000, 25000));
    }

    // Only the map's one field of the channel opens them, as the field interact events.
    [TestCase(InstanceType.solo, 0)]
    [TestCase(InstanceType.channelScale, 1)]
    public void OnlyAChannelFieldOpensThem(InstanceType instance, int count) {
        FieldManager field = Field(instance, Row());
        field.UpdateRoomEvents(Start.AddSeconds(1));
        Assert.That(Rooms(field), Has.Count.EqualTo(count));
    }
}
