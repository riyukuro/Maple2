using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

// Condition types the server counts, checked against conditions shaped like the quest data's.
public class ConditionCountTests {
    private const int MapId = 2000001;

    // A kill counts for a spawner condition when the mob's region spawn is one of its codes, on the map of its target.
    [TestCase(2, MapId, true)]
    [TestCase(5, MapId, false)]
    [TestCase(2, MapId + 1, false)]
    public void Spawner_CountsTheListedSpawnsOfTheMap(int spawnId, int mapId, bool expected) {
        var condition = new ConditionMetadata(ConditionType.spawner, 10,
            new ConditionMetadata.Parameters(Integers: [1, 2, 3, 4]),
            new ConditionMetadata.Parameters(Integers: [MapId]));
        Assert.That(condition.Check(null!, targetLong: mapId, codeLong: spawnId), Is.EqualTo(expected));
    }

    [TestCase(26, true)]
    [TestCase(42, true)]
    [TestCase(43, false)]
    public void Spawner_CountsASpawnRange(int spawnId, bool expected) {
        var condition = new ConditionMetadata(ConditionType.spawner, 30,
            new ConditionMetadata.Parameters(Range: new ConditionMetadata.Range<int>(26, 42)),
            new ConditionMetadata.Parameters(Integers: [MapId]));
        Assert.That(condition.Check(null!, targetLong: MapId, codeLong: spawnId), Is.EqualTo(expected));
    }

    // A dungeon clear counts for dungeon_clear_group when the room's group type is one of its codes.
    [TestCase(DungeonGroupType.normal, true)]
    [TestCase(DungeonGroupType.raid, true)]
    [TestCase(DungeonGroupType.chaosRaid, false)]
    public void DungeonClearGroup_CountsTheListedGroups(DungeonGroupType groupType, bool expected) {
        var condition = new ConditionMetadata(ConditionType.dungeon_clear_group, 1,
            new ConditionMetadata.Parameters(Integers: [1, 2]), null);
        Assert.That(condition.Check(null!, codeLong: (long) groupType), Is.EqualTo(expected));
    }

    private const int HomeMapId = 62000000;

    // Entering a home counts for enter_otherhouse only when another account owns it.
    [TestCase(1001L, 1002L, true)]
    [TestCase(1001L, 1001L, false)]
    [TestCase(0L, 1001L, false)]
    public void EnterOtherHouse_OnlyAnotherAccountsHome(long ownerId, long accountId, bool expected) {
        Assert.That(FieldEntryRules.VisitsOtherHome(ownerId, HomeMapId, accountId, MapId, 0), Is.EqualTo(expected));
    }

    // Coming from that same home is no new visit; from another map, another home or no field (a new session) it is.
    [TestCase(HomeMapId, 1001L, false)]
    [TestCase(HomeMapId, 1003L, true)]
    [TestCase(MapId, 0L, true)]
    [TestCase(0, 0L, true)]
    public void EnterOtherHouse_NotFromTheSameHome(int fromMapId, long fromOwnerId, bool expected) {
        Assert.That(FieldEntryRules.VisitsOtherHome(1001, HomeMapId, 1002, fromMapId, fromOwnerId), Is.EqualTo(expected));
    }

    [Test]
    public void EnterOtherHouse_ConditionWithoutCodesCounts() {
        var condition = new ConditionMetadata(ConditionType.enter_otherhouse, 3, null, null);
        Assert.That(condition.Check(null!), Is.True);
    }
}
