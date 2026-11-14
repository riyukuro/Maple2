using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game;

// timeEventData type 4: the interact objects a field puts on its region spawn points. Field mission 72000011 asks
// for interact 14000005, which only row 20006 puts on map 02000003 (point 3000 and the points tagged rare treasure
// chest 1); before, no server code read type 4 and the quest could not be completed.
public class FieldInteractEventTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int MapId = 2000003;
    private const int QuestId = 72000011;
    private const int InteractId = 14000005;
    private const int Point = 3000;
    // The rare chest tag of the row and of the map's point (KR data).
    private const string RareChestTag = "\ub808\uc5b4 \ubcf4\ubb3c \uc0c1\uc790_1";

    private static readonly DateTime Start = new(2018, 1, 1);

    private static FieldInteractEventMetadata Row(int id = 20006, int[]? points = null, string[]? tags = null, int count = 0, float variable = 0.5f,
        int probability = 100, TimeSpan? cycle = null, TimeSpan? life = null, int interactId = InteractId) {
        return new FieldInteractEventMetadata(id, probability, Start, new DateTime(2099, 1, 1), cycle ?? TimeSpan.FromMinutes(15), TimeSpan.Zero,
            life ?? new TimeSpan(0, 14, 55), [MapId], points ?? [Point], tags ?? [RareChestTag], count, variable, interactId,
            "", "", "", "", 0, 0, 0, 0);
    }

    private static MapMetadataSpawn Spawn(int id, params string[] tags) => new(id, 0, 0, 0, 0, tags, 0, 0, new Dictionary<int, int>());

    // A point that is no chest point (SpawnKind None).
    private const int PlainPoint = 5000;
    // An interact object that is no trophy id of the map.
    private const int OtherInteractId = 14000099;

    private static readonly FieldInfo GoldChestIdsField = typeof(FieldManager).GetField("goldChestIds", BindingFlags.Static | BindingFlags.NonPublic)!;

    public enum Place { Map, Dungeon, Home }

    private TestGameSession test = null!;

    // The golden chest trophy ids are a static cache of FieldManager: no test leaves its own there.
    [TearDown]
    public void ClearGoldChestIds() => GoldChestIdsField.SetValue(null, null);

    private static InteractObjectMetadata Interact(int id, int resetTime) =>
        new(id, InteractType.Mesh, Collection: 0, ReactCount: 0, TargetPortalId: 0, GuildPosterId: 0,
            WeaponItemId: 0, new InteractObjectMetadataItem(0, 0, 0, 0, 0), new InteractObjectMetadataTime(resetTime, 1000, 10000),
            new InteractObjectMetadataDrop(0, [], [], 0, 0), new InteractObjectMetadataEffect([], [], 0, 0), []);

    private FieldManager Field(params FieldInteractEventMetadata[] rows) => Field(Place.Map, null, rows);

    private FieldManager Field(Place place, InstanceType? instance, params FieldInteractEventMetadata[] rows) {
        test = new TestGameSession();
        test.Start();
        // 14000005: a mesh object, reset 1200000, react 1000, hide 10000 (as interactobject.xml); 14000099 resets
        // after 30 s (as the KR rows of 10000264 on map 02000138).
        test.SetTable(new InteractObjectTable(new Dictionary<int, InteractObjectMetadata> {
            [InteractId] = Interact(InteractId, 1200000),
            [OtherInteractId] = Interact(OtherInteractId, 30000),
        }));
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

        switch (place) {
            case Place.Dungeon:
                test.EnterDungeon(TestGameSession.DungeonRoom(1, MapId));
                break;
            case Place.Home:
                test.EnterHome();
                break;
            default:
                test.EnterMap(TestGameSession.Map(MapId) with {
                    Spawns = [Spawn(Point, RareChestTag), Spawn(3001, RareChestTag), Spawn(3002, "other"), Spawn(3003)],
                    Drop = new MapMetadataDrop(1, 0, []),
                });
                break;
        }
        FieldManager field = test.Session.Field!;
        // The rows name the field's map (a home has its own).
        test.SetServerTable(new TimeEventTable(new Dictionary<int, GlobalPortalMetadata>(), new Dictionary<int, WorldBossMetadata>(),
            FieldInteractEvent: rows.ToDictionary(row => row.Id, row => row with { MapIds = [field.MapId] })));
        typeof(FieldManager).GetField("fieldChests", Any)!.SetValue(field, new ConcurrentDictionary<string, FieldInteract>());
        var entities = (MapEntityMetadata) RuntimeHelpers.GetUninitializedObject(typeof(MapEntityMetadata));
        typeof(MapEntityMetadata).GetField("<RegionSpawns>k__BackingField", Any)!.SetValue(entities, new Dictionary<int, Ms2RegionSpawn> {
            [Point] = new(Point, false, new Vector3(150, 300, 450), Vector3.Zero),
            [3001] = new(3001, false, new Vector3(1500, 300, 450), Vector3.Zero),
            [3002] = new(3002, false, new Vector3(3000, 300, 450), Vector3.Zero),
            [3003] = new(3003, false, new Vector3(4500, 300, 450), Vector3.Zero),
            [PlainPoint] = new(PlainPoint, false, new Vector3(6000, 300, 450), Vector3.Zero),
        });
        typeof(FieldManager).GetField("<Entities>k__BackingField", Any)!.SetValue(field, entities);
        // The guessed map chests are not read here (they need the achievement and combine spawn tables).
        typeof(FieldManager).GetField("mapChestsRead", Any)!.SetValue(field, true);
        typeof(FieldManager).GetField("woodChestPoints", Any)!.SetValue(field, new List<int>());
        typeof(FieldManager).GetField("goldChestPoints", Any)!.SetValue(field, new Dictionary<int, int>());
        return field;
    }

    private static ConcurrentDictionary<string, FieldInteract> Chests(FieldManager field) =>
        (ConcurrentDictionary<string, FieldInteract>) typeof(FieldManager).GetField("fieldChests", Any)!.GetValue(field)!;

    private static void Open(GameSession session, string entityId) {
        new InteractObjectHandler().Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(12);
            writer.WriteInt(1);
            writer.WriteString(entityId);
        }));
    }

    // 72000011 asks interact_object_rep 14000005 x1 (the KR quest row), which counts every open; a character that
    // opened the golden chest before (its id in the interacted objects) still completes it.
    [TestCase(false)]
    [TestCase(true)]
    public void FieldMission72000011_IsCompletableOnItsMap(bool openedBefore) {
        FieldManager field = Field(Row());
        if (openedBefore) {
            test.Session.Player.Value.Unlock.InteractedObjects.Add(InteractId);
        }
        Quest quest = test.StartQuest(TestGameSession.QuestData(QuestId, QuestType.FieldMission) with {
            Conditions = [new ConditionMetadata(ConditionType.interact_object_rep, 1, new ConditionMetadata.Parameters(Integers: [InteractId]), null)],
        });

        // Mid-run at field load: the run of 00:15 is alive until 00:29:55.
        field.UpdateInteractEvents(Start.AddMinutes(20));

        List<FieldInteract> placed = Chests(field).Values.ToList();
        Assert.That(placed.Select(interact => interact.Object.Id), Has.Some.EqualTo(InteractId));
        FieldInteract chest = placed.First(interact => interact.SpawnId == Point || interact.SpawnId == 3001);
        Open(test.Session, chest.EntityId);

        Assert.Multiple(() => {
            Assert.That(test.Errors, Is.Empty);
            Assert.That(quest.Conditions[0].Counter, Is.EqualTo(1));
            Assert.That(quest.State, Is.EqualTo(QuestState.Completed));
        });
    }

    [Test]
    public void AnObjectIsPlacedUsedRemovedAndReplacedAtTheNextRun() {
        FieldManager field = Field(Row(count: 1, tags: []));
        field.UpdateInteractEvents(Start.AddMinutes(20));
        FieldInteract chest = Chests(field).Values.Single();
        Assert.That(chest.SpawnId, Is.EqualTo(Point));

        Assert.That(chest.React(), Is.True);
        // A chest point: opened once, it hides after its hide time and the field takes it away.
        chest.Update(Environment.TickCount64 + 10001);
        Assert.That(chest.State, Is.EqualTo(InteractState.Hidden));
        typeof(FieldManager).GetMethod("UpdateChests", Any)!.Invoke(field, [field.FieldTick]);
        Assert.That(Chests(field), Is.Empty);

        // Nothing until the next run (00:30), then a new one on the free point.
        field.UpdateInteractEvents(Start.AddMinutes(29));
        Assert.That(Chests(field), Is.Empty);
        field.UpdateInteractEvents(Start.AddMinutes(30));
        FieldInteract next = Chests(field).Values.Single();
        Assert.Multiple(() => {
            Assert.That(next.SpawnId, Is.EqualTo(Point));
            Assert.That(next, Is.Not.SameAs(chest));
            Assert.That(next.State, Is.EqualTo(InteractState.Reactable));
        });

        // At the end of its life (00:44:55) it leaves.
        field.UpdateInteractEvents(Start.AddMinutes(44).AddSeconds(55));
        Assert.That(Chests(field), Is.Empty);
    }

    [Test]
    public void NoRunOutsideTheLifeOrWithoutProb() {
        FieldManager field = Field(Row(life: TimeSpan.FromMinutes(5)), Row(id: 20007, probability: 0));
        // 00:20: the run of 00:15 left at 00:20; the prob 0 row never runs.
        field.UpdateInteractEvents(Start.AddMinutes(20));
        Assert.That(Chests(field), Is.Empty);
        field.UpdateInteractEvents(Start.AddMinutes(30));
        Assert.That(Chests(field).Values.Select(interact => interact.TimeEventId).Distinct(), Is.EqualTo(new[] { 20006 }));
    }

    // An empty field is not updated: a run that came due meanwhile gets only what is left of its life, and nothing
    // once its life is over; the next grid run still comes.
    [Test]
    public void ARunDueWhileTheFieldWasEmptyKeepsItsLife() {
        FieldManager field = Field(Row(count: 1, tags: [], life: TimeSpan.FromMinutes(5)));
        field.UpdateInteractEvents(Start.AddMinutes(11));
        field.UpdateInteractEvents(Start.AddMinutes(17));
        Assert.That(Chests(field), Has.Count.EqualTo(1));
        // the run of 00:15 leaves at 00:20, not 5 minutes after the late update
        field.UpdateInteractEvents(Start.AddMinutes(20));
        Assert.That(Chests(field), Is.Empty);
    }

    [Test]
    public void ARunOverWhileTheFieldWasEmptyIsNotPlaced() {
        FieldManager field = Field(Row(count: 1, tags: [], life: TimeSpan.FromMinutes(5)));
        field.UpdateInteractEvents(Start.AddMinutes(11));
        field.UpdateInteractEvents(Start.AddMinutes(23));
        Assert.That(Chests(field), Is.Empty);
        field.UpdateInteractEvents(Start.AddMinutes(30));
        Assert.That(Chests(field), Has.Count.EqualTo(1));
    }

    // rv241 #3: a type 4 object on a point that is no chest point is opened once in its run, whatever its resetTime.
    [Test]
    public void AnObjectOnAPlainPointIsNotOpenedAgainInItsRun() {
        FieldManager field = Field(Row(points: [PlainPoint], tags: [], count: 1, interactId: OtherInteractId));
        field.UpdateInteractEvents(Start.AddMinutes(20));
        FieldInteract interact = Chests(field).Values.Single();
        Assert.That(interact.Chest, Is.EqualTo(MapChestRules.Kind.None));

        Assert.That(interact.React(), Is.True);
        // Past its resetTime (30 s) and its hide time it is gone, not reactable again.
        interact.Update(Environment.TickCount64 + 30001);
        interact.Update(Environment.TickCount64 + 60001);
        Assert.Multiple(() => {
            Assert.That(interact.State, Is.EqualTo(InteractState.Hidden));
            Assert.That(interact.React(), Is.False);
        });
    }

    // A second open of a used object (by the same or another player) counts nothing.
    [Test]
    public void ASecondOpenOfAUsedObjectCountsNothing() {
        FieldManager field = Field(Row(count: 1, tags: []));
        Quest quest = test.StartQuest(TestGameSession.QuestData(QuestId, QuestType.FieldMission) with {
            Conditions = [new ConditionMetadata(ConditionType.interact_object_rep, 2, new ConditionMetadata.Parameters(Integers: [InteractId]), null)],
        });
        field.UpdateInteractEvents(Start.AddMinutes(20));
        FieldInteract chest = Chests(field).Values.Single();

        Open(test.Session, chest.EntityId);
        Open(test.Session, chest.EntityId);
        Assert.Multiple(() => {
            Assert.That(quest.Conditions[0].Counter, Is.EqualTo(1));
            Assert.That(quest.State, Is.EqualTo(QuestState.Started));
            Assert.That(chest.State, Is.Not.EqualTo(InteractState.Reactable));
        });
    }

    // rv241 #4: a field made again in a run its map placed already (the old one disposed when empty) places nothing
    // until the next run.
    [Test]
    public void AFieldMadeAgainInTheRunDoesNotPlaceItAgain() {
        FieldManager first = Field(Row(count: 1, tags: []));
        first.UpdateInteractEvents(Start.AddMinutes(20));
        Assert.That(Chests(first), Has.Count.EqualTo(1));
        var placed = first.InteractEventRunsPlaced;

        FieldManager second = Field(Row(count: 1, tags: []));
        second.InteractEventRunsPlaced = placed;
        second.UpdateInteractEvents(Start.AddMinutes(21));
        Assert.That(Chests(second), Is.Empty);
        second.UpdateInteractEvents(Start.AddMinutes(30));
        Assert.That(Chests(second), Has.Count.EqualTo(1));
    }

    // rv241 #5: no row runs in a dungeon, a home or an instanced map but one of channelScale.
    [TestCase(Place.Map, null, 1)]
    [TestCase(Place.Map, InstanceType.channelScale, 1)]
    [TestCase(Place.Map, InstanceType.solo, 0)]
    [TestCase(Place.Dungeon, null, 0)]
    [TestCase(Place.Home, null, 0)]
    public void RowsRunOnlyOnTheMapsOwnField(Place place, InstanceType? instance, int expected) {
        FieldManager field = Field(place, instance, Row(count: 1, tags: []));
        field.UpdateInteractEvents(Start.AddMinutes(20));
        Assert.That(Chests(field), Has.Count.EqualTo(expected));
    }

    // A field with no players does no work: Update returns before the rows are read.
    [Test]
    public void AFieldWithoutPlayersDoesNoWork() {
        FieldManager field = Field(Row(count: 1, tags: [], cycle: TimeSpan.FromMinutes(1), life: TimeSpan.FromMinutes(1)));
        FieldInfo queued = typeof(FieldManager).GetField("queuedPackets", Any)!;
        queued.SetValue(field, Activator.CreateInstance(queued.FieldType));
        field.Players.Clear();
        field.Update();
        Assert.Multiple(() => {
            Assert.That(typeof(FieldManager).GetField("interactEvents", Any)!.GetValue(field), Is.Null);
            Assert.That(Chests(field), Is.Empty);
        });
    }

    // rv241 #2 and rule 5 of ReadMapChests: a live row's golden chest points get no guessed chest; a row that never
    // runs (no prob) leaves them to the map chests; a row whose interact id is no trophy id of the map holds no
    // golden chest point.
    [TestCase(100, InteractId, 0)]
    [TestCase(0, InteractId, 2)]
    [TestCase(100, OtherInteractId, 2)]
    public void ReadMapChests_LeavesTheRowsGoldenChestPointsToTheRow(int probability, int interactId, int goldChests) {
        FieldManager field = Field(Row(probability: probability, interactId: interactId));
        GoldChestIdsField.SetValue(null, new Dictionary<int, int[]> { [MapId] = [InteractId, 14000006] });
        typeof(FieldManager).GetField("<AchievementMetadata>k__BackingField", Any)!.SetValue(field,
            typeof(GameSession).GetProperty(nameof(GameSession.AchievementMetadata))!.GetValue(test.Session));
        test.SetServerTable(new CombineSpawnTable(new Dictionary<int, Dictionary<int, SpawnGroupMetadata>>(),
            new Dictionary<int, Dictionary<int, SpawnNpcMetadata>>(), new Dictionary<int, Dictionary<int, SpawnInteractObjectMetadata>>()));
        typeof(FieldManager).GetField("mapChestsRead", Any)!.SetValue(field, false);
        typeof(FieldManager).GetMethod("ReadMapChests", Any)!.Invoke(field, []);

        var points = (Dictionary<int, int>) typeof(FieldManager).GetField("goldChestPoints", Any)!.GetValue(field)!;
        Assert.That(points, Has.Count.EqualTo(goldChests));
        if (goldChests > 0) {
            Assert.That(points, Does.ContainKey(Point).WithValue(InteractId));
        }
        // The other interact id holds no golden chest point, so the row puts nothing on the map.
        field.UpdateInteractEvents(Start.AddMinutes(20));
        Assert.That(Chests(field).Values.Where(interact => interact.TimeEventId != 0), interactId == OtherInteractId || probability == 0 ? Is.Empty : Is.Not.Empty);
    }

    [Test]
    public void Points_AreTheRowsPointsAndTaggedPointsTheMapHas() {
        FieldInteractEventMetadata row = Row(points: [Point, 9999]);
        List<MapMetadataSpawn> spawns = [Spawn(Point, RareChestTag), Spawn(3001, " " + RareChestTag), Spawn(3002, "other"), Spawn(3004, RareChestTag)];
        Assert.That(FieldInteractEventRules.Points(row, spawns, new HashSet<int> { Point, 3001, 3002, 3003 }), Is.EqualTo(new[] { Point, 3001 }));
    }

    [TestCase(10, 0.5f, 4, 4)]
    [TestCase(2, 0.5f, 4, 2)]
    [TestCase(0, 0.5f, 3, 2)]
    [TestCase(0, 0.5f, 1, 1)]
    [TestCase(0, 0.125f, 4, 1)]
    [TestCase(0, 0.1f, 10, 1)]
    [TestCase(0, 0f, 6, 1)]
    [TestCase(0, 0.5f, 0, 0)]
    public void Count_IsTheExplicitCountElseTheShareOfPoints(int count, float variable, int points, int expected) {
        Assert.That(FieldInteractEventRules.Count(Row(count: count, variable: variable), points), Is.EqualTo(expected));
    }

    [Test]
    public void Pick_TakesDistinctFreePoints() {
        var random = new Random(1);
        for (int i = 0; i < 20; i++) {
            List<int> picked = FieldInteractEventRules.Pick([1, 2, 3, 4, 5], 3, random);
            Assert.That(picked, Has.Count.EqualTo(3).And.Unique.And.All.InRange(1, 5));
        }
        Assert.That(FieldInteractEventRules.Pick([1, 2], 5, random), Is.EquivalentTo(new[] { 1, 2 }));
    }

    [Test]
    public void NextAndCurrentRun_FollowTheCycleGrid() {
        FieldInteractEventMetadata row = Row();
        Assert.Multiple(() => {
            Assert.That(FieldInteractEventRules.NextRun(row, Start.AddDays(-1)), Is.EqualTo(Start));
            Assert.That(FieldInteractEventRules.NextRun(row, Start), Is.EqualTo(Start.AddMinutes(15)));
            Assert.That(FieldInteractEventRules.NextRun(row, Start.AddMinutes(20)), Is.EqualTo(Start.AddMinutes(30)));
            Assert.That(FieldInteractEventRules.NextRun(row with { EndTime = Start.AddMinutes(30) }, Start.AddMinutes(20)), Is.Null);
            Assert.That(FieldInteractEventRules.CurrentRun(row, Start.AddMinutes(20)), Is.EqualTo(Start.AddMinutes(15)));
            Assert.That(FieldInteractEventRules.CurrentRun(row, Start.AddMinutes(29).AddSeconds(55)), Is.Null);
            Assert.That(FieldInteractEventRules.CurrentRun(row, Start.AddDays(-1)), Is.Null);
        });
    }

    [Test]
    public void InWindow_ReadsTheDurationHoursAcrossMidnight() {
        FieldInteractEventMetadata day = Row() with { DurationStartHour = 13, DurationEndHour = 21, DurationEndMin = 59 };
        FieldInteractEventMetadata night = Row() with { DurationStartHour = 22, DurationEndHour = 12, DurationEndMin = 59 };
        DateTime date = new(2026, 11, 13);
        Assert.Multiple(() => {
            Assert.That(FieldInteractEventRules.InWindow(Row(), date.AddHours(3)), Is.True);
            Assert.That(FieldInteractEventRules.InWindow(day, date.AddHours(13)), Is.True);
            Assert.That(FieldInteractEventRules.InWindow(day, date.AddHours(21).AddMinutes(59)), Is.True);
            Assert.That(FieldInteractEventRules.InWindow(day, date.AddHours(22)), Is.False);
            Assert.That(FieldInteractEventRules.InWindow(night, date.AddHours(22)), Is.True);
            Assert.That(FieldInteractEventRules.InWindow(night, date.AddHours(3)), Is.True);
            Assert.That(FieldInteractEventRules.InWindow(night, date.AddHours(13)), Is.False);
        });
    }

    [TestCase(100, 99, true)]
    [TestCase(0, 0, false)]
    [TestCase(30, 29, true)]
    [TestCase(30, 30, false)]
    public void Runs_RollsTheProb(int probability, int roll, bool expected) {
        Assert.That(FieldInteractEventRules.Runs(probability, roll), Is.EqualTo(expected));
    }
}
