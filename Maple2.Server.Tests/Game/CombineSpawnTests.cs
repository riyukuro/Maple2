using System.Collections.Generic;
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
    public void TotalCountIsCappedAtDistinctSpawns() {
        Assert.That(SpawnGroupRules.TotalCount(3, [501, 502]), Is.EqualTo(2));
        Assert.That(SpawnGroupRules.TotalCount(3, [501, 501, 502]), Is.EqualTo(2));
        Assert.That(SpawnGroupRules.TotalCount(1, []), Is.EqualTo(0));
        Assert.That(SpawnGroupRules.TotalCount(0, [501]), Is.EqualTo(0));
        Assert.That(SpawnGroupRules.TotalCount(1, [501, 502]), Is.EqualTo(1));
    }
}
