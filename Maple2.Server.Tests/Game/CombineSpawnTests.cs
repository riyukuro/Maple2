using System.Collections.Generic;
using System.Numerics;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game;

// start_combine_spawn takes a group from the field's own map, or from the map that lists it when the field has
// every spawn the group names.
public class CombineSpawnTests {
    private static CombineSpawnTable Table() {
        var groups = new Dictionary<int, Dictionary<int, SpawnGroupMetadata>> {
            [82000001] = new() {
                [10] = new SpawnGroupMetadata(10, CombineSpawnGroupType.npc, 1, 120000, 82000001),
                [20] = new SpawnGroupMetadata(20, CombineSpawnGroupType.interactObject, 1, 170000, 82000001),
            },
            [82000012] = new() {
                [30] = new SpawnGroupMetadata(30, CombineSpawnGroupType.npc, 2, 0, 82000012),
            },
        };
        var npcs = new Dictionary<int, Dictionary<int, SpawnNpcMetadata>> {
            [10] = new() { [1] = new SpawnNpcMetadata(1, 10, 100, 501), [2] = new SpawnNpcMetadata(2, 10, 100, 502) },
            [30] = new() { [1] = new SpawnNpcMetadata(1, 30, 100, 900) },
        };
        var objects = new Dictionary<int, Dictionary<int, SpawnInteractObjectMetadata>> {
            [20] = new() { [1] = new SpawnInteractObjectMetadata(1, 20, 100, 7001, 11000038, "", "", "", "", 1f, false) },
        };
        return new CombineSpawnTable(groups, npcs, objects);
    }

    private static readonly HashSet<int> CopyNpcSpawns = [501, 502];
    private static readonly HashSet<int> CopyRegions = [7001];

    [Test]
    public void OwnMapGroupIsTaken() {
        CombineSpawnRules.Source source = CombineSpawnRules.Find(Table(), 82000012, 30, _ => false, _ => false, out SpawnGroupMetadata? group);
        Assert.That(source, Is.EqualTo(CombineSpawnRules.Source.Own));
        Assert.That(group?.GroupId, Is.EqualTo(30));
    }

    [Test]
    public void CopyMapTakesGroupsWhoseSpawnsItHas() {
        // 82000000 lists no group but has 82000001's spawn points and region spawns.
        Assert.That(CombineSpawnRules.Find(Table(), 82000000, 10, CopyNpcSpawns.Contains, CopyRegions.Contains, out SpawnGroupMetadata? npcGroup), Is.EqualTo(CombineSpawnRules.Source.OtherMap));
        Assert.That(npcGroup?.MapId, Is.EqualTo(82000001));
        Assert.That(CombineSpawnRules.Find(Table(), 82000000, 20, CopyNpcSpawns.Contains, CopyRegions.Contains, out SpawnGroupMetadata? boxGroup), Is.EqualTo(CombineSpawnRules.Source.OtherMap));
        Assert.That(boxGroup?.Type, Is.EqualTo(CombineSpawnGroupType.interactObject));
    }

    [Test]
    public void GroupWithSpawnsOffTheFieldIsSkipped() {
        // Only one of group 10's two spawn points is on the field.
        Assert.That(CombineSpawnRules.Find(Table(), 82000000, 10, id => id == 501, CopyRegions.Contains, out SpawnGroupMetadata? group), Is.EqualTo(CombineSpawnRules.Source.NotOnField));
        Assert.That(group, Is.Null);
        Assert.That(CombineSpawnRules.Find(Table(), 82000000, 30, CopyNpcSpawns.Contains, CopyRegions.Contains, out _), Is.EqualTo(CombineSpawnRules.Source.NotOnField));
    }

    [Test]
    public void UnknownGroupIsMissing() {
        Assert.That(CombineSpawnRules.Find(Table(), 82000002, 99, _ => true, _ => true, out SpawnGroupMetadata? group), Is.EqualTo(CombineSpawnRules.Source.Missing));
        Assert.That(group, Is.Null);
    }

    [Test]
    public void GroupPointsOfTheOwnMap() {
        // 82000012 lists group 30; 82000001's group 10 is not on it (its points are not there).
        Assert.That(CombineSpawnRules.GroupSpawnIds(Table(), 82000012, id => id == 900, []), Is.EquivalentTo(new[] { 900 }));
        Assert.That(CombineSpawnRules.GroupSpawnIds(Table(), 82000001, CopyNpcSpawns.Contains, []), Is.EquivalentTo(new[] { 501, 502 }));
    }

    [Test]
    public void GroupPointsOfACopyMap() {
        // 82000000's triggers start groups 10, 20 and 30: it has both of group 10's points; group 30's point is not
        // on it, and box groups name no point.
        Assert.That(CombineSpawnRules.GroupSpawnIds(Table(), 82000000, id => CopyNpcSpawns.Contains(id) || id == 777, [10, 20, 30, 10]), Is.EquivalentTo(new[] { 501, 502 }));
    }

    [Test]
    public void ACopyMapTakesOnlyTheGroupsItsTriggersStart() {
        // A map with points 501 and 502 by chance (small ids are reused) whose triggers start no group, or only an
        // unknown one, keeps them as points that fill themselves.
        Assert.That(CombineSpawnRules.GroupSpawnIds(Table(), 2000402, CopyNpcSpawns.Contains, []), Is.Empty);
        Assert.That(CombineSpawnRules.GroupSpawnIds(Table(), 82000002, CopyNpcSpawns.Contains, [10000382]), Is.Empty);
    }

    [Test]
    public void AMapWithItsOwnGroupsTakesNoOtherMapsPoints() {
        // 82000012 has 82000001's points 501 and 502 too, but lists group 30 of its own.
        Assert.That(CombineSpawnRules.GroupSpawnIds(Table(), 82000012, id => CopyNpcSpawns.Contains(id) || id == 900, [10]), Is.EquivalentTo(new[] { 900 }));
    }

    [Test]
    public void NoGroupPointsWhenAGroupIsOnlyPartlyOnTheField() {
        // Point 501 alone stays a point that fills itself.
        Assert.That(CombineSpawnRules.GroupSpawnIds(Table(), 82000000, id => id == 501, [10]), Is.Empty);
        Assert.That(CombineSpawnRules.GroupSpawnIds(Table(), 2000001, _ => false, [10]), Is.Empty);
    }

    [Test]
    public void AResetRefillsOnlyTheKilledPick() {
        // A group keeping 2 of its points up: the start puts up 501 and 502; 501's npc is killed, so the
        // next reset draws again and puts up a point that is not up (503), leaving 502 as it is.
        var up = new List<int>();
        var draws = new Queue<int>([501, 502]);
        var spawned = new List<int>();
        Assert.That(SpawnGroupRules.Refill(up, _ => true, 2, Points, draws.Dequeue, spawned.Add).Added, Is.EqualTo(new[] { 501, 502 }));

        draws = new Queue<int>([502, 503]);
        List<int> added = SpawnGroupRules.Refill(up, spawnId => spawnId != 501, 2, Points, draws.Dequeue, spawned.Add).Added;
        Assert.Multiple(() => {
            Assert.That(added, Is.EqualTo(new[] { 503 }));
            Assert.That(up, Is.EquivalentTo(new[] { 502, 503 }));
            Assert.That(spawned, Is.EqualTo(new[] { 501, 502, 503 }));
        });

        // Nothing killed: a reset puts nothing up.
        Assert.That(SpawnGroupRules.Refill(up, _ => true, 2, Points, () => throw new AssertionException("no draw"), spawned.Add).Added, Is.Empty);
    }

    private static readonly int[] Points = [501, 502, 503];

    [Test]
    public void AFillStopsAtItsPointsAndItsDraws() {
        // totalCount above the group's points (as 02020051's group 1003: 38 for 37 points): every point goes up and
        // the fill stops there instead of drawing for ever.
        var up = new List<int>();
        int next = 0;
        SpawnGroupRules.Fill fill = SpawnGroupRules.Refill(up, _ => true, 5, Points, () => Points[next++ % Points.Length], _ => { });
        Assert.Multiple(() => {
            Assert.That(fill.Added, Is.EqualTo(Points));
            Assert.That(fill.GaveUp, Is.False);
        });

        // A pick that never draws the last point (a weight of 0): the fill gives up after its draws, with what it has.
        up.Clear();
        int drawn = 0;
        fill = SpawnGroupRules.Refill(up, _ => true, 3, Points, () => Points[drawn++ % 2], _ => { });
        Assert.Multiple(() => {
            Assert.That(fill.Added, Is.EqualTo(new[] { 501, 502 }));
            Assert.That(fill.GaveUp, Is.True);
            Assert.That(drawn, Is.EqualTo(3 * SpawnGroupRules.DrawsPerSpawn));
        });

        // An empty group draws nothing.
        Assert.That(SpawnGroupRules.Refill([], _ => true, 2, [], () => throw new AssertionException("no draw"), _ => { }).Added, Is.Empty);
    }

    [Test]
    public void AGroupPointNeverFillsItself() {
        SpawnPointNPC point = new("SpawnPointNPC_501", 501, Vector3.Zero, Vector3.Zero, true, true, 0f, [], 30, null);
        Assert.Multiple(() => {
            Assert.That(SpawnGroupRules.SelfFills(point, groupSpawned: false), Is.True);
            Assert.That(SpawnGroupRules.SpawnsOnCreate(point, groupSpawned: false), Is.True);
            Assert.That(SpawnGroupRules.SelfFills(point, groupSpawned: true), Is.False);
            Assert.That(SpawnGroupRules.SpawnsOnCreate(point, groupSpawned: true), Is.False);
            Assert.That(SpawnGroupRules.SelfFills(point with { RegenCheckTime = 0 }, groupSpawned: false), Is.False);
        });
    }

    [Test]
    public void TotalCountIsCappedAtDistinctSpawns() {
        Assert.That(SpawnGroupRules.TotalCount(3, [501, 502]), Is.EqualTo(2));
        Assert.That(SpawnGroupRules.TotalCount(3, [501, 501, 502]), Is.EqualTo(2));
        Assert.That(SpawnGroupRules.TotalCount(1, []), Is.EqualTo(0));
        Assert.That(SpawnGroupRules.TotalCount(0, [501]), Is.EqualTo(0));
        Assert.That(SpawnGroupRules.TotalCount(1, [501, 502]), Is.EqualTo(1));
    }
}
