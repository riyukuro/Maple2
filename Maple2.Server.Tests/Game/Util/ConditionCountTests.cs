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

    // An item counts for an item_exist row with a map target only when collected on that map; a row without a
    // target counts it anywhere.
    [TestCase(MapId, true)]
    [TestCase(MapId + 1, false)]
    public void ItemExist_CountsOnTheTargetMap(int mapId, bool expected) {
        var withTarget = new ConditionMetadata(ConditionType.item_exist, 5,
            new ConditionMetadata.Parameters(Integers: [30000285]), new ConditionMetadata.Parameters(Integers: [MapId]));
        var noTarget = new ConditionMetadata(ConditionType.item_exist, 5,
            new ConditionMetadata.Parameters(Integers: [30000285]), null);
        Assert.That(withTarget.Check(null!, targetLong: mapId, codeLong: 30000285), Is.EqualTo(expected));
        Assert.That(noTarget.Check(null!, targetLong: mapId, codeLong: 30000285), Is.True);
    }

    // The killing blow on an NPC counts for npc_lasthit when the NPC is one of the row's codes.
    [TestCase(23000024, true)]
    [TestCase(23000018, false)]
    public void NpcLastHit_CountsTheListedNpc(int npcId, bool expected) {
        var condition = new ConditionMetadata(ConditionType.npc_lasthit, 1,
            new ConditionMetadata.Parameters(Integers: [23000024]), null);
        Assert.That(condition.Check(null!, targetLong: MapId, codeLong: npcId), Is.EqualTo(expected));
    }

    // Taking furniture into use counts for controller when its item id is one of the row's codes.
    [TestCase(50400002, true)]
    [TestCase(50400004, false)]
    public void Controller_CountsTheListedFurniture(int itemId, bool expected) {
        var condition = new ConditionMetadata(ConditionType.controller, 1,
            new ConditionMetadata.Parameters(Integers: [50400002]), null);
        Assert.That(condition.Check(null!, codeLong: itemId), Is.EqualTo(expected));
    }

    // A completed field mission counts for field_mission when its quest id is one of the row's codes or in its range.
    [TestCase(72000150, true)]
    [TestCase(72000740, true)]
    [TestCase(72000154, false)]
    public void FieldMission_CountsTheListedMissions(int questId, bool expected) {
        var condition = new ConditionMetadata(ConditionType.field_mission, 1,
            new ConditionMetadata.Parameters(Integers: [72000149, 72000150], Range: new ConditionMetadata.Range<int>(72000736, 72000806)), null);
        Assert.That(condition.Check(null!, codeLong: questId), Is.EqualTo(expected));
    }

    // Ensemble play time counts for play_ensenble_time when the map played on is the row's code.
    [TestCase(2000064, true)]
    [TestCase(2000001, false)]
    public void PlayEnsembleTime_CountsOnTheListedMap(int mapId, bool expected) {
        var condition = new ConditionMetadata(ConditionType.play_ensenble_time, 300,
            new ConditionMetadata.Parameters(Integers: [2000064]), null);
        Assert.That(condition.Check(null!, targetLong: mapId, codeLong: mapId), Is.EqualTo(expected));
    }

    // An achievement reaching a grade counts for hero_achieve when the achievement is one of the row's codes and the
    // grade its target (quests 71000003 and 71000004 ask for grades 1 and 2 of one trophy).
    [TestCase(23100006, 2, true)]
    [TestCase(23100006, 1, false)]
    [TestCase(23100007, 2, false)]
    public void HeroAchieve_CountsTheListedAchievementGrade(int achievementId, int grade, bool expected) {
        var condition = new ConditionMetadata(ConditionType.hero_achieve, 1,
            new ConditionMetadata.Parameters(Integers: [23100006]), new ConditionMetadata.Parameters(Integers: [2]));
        Assert.That(condition.Check(null!, targetLong: grade, codeLong: achievementId), Is.EqualTo(expected));
    }

    // An interact object counts for interact_object_rep with a target when its interact type is the target.
    [TestCase(InteractType.Telescope, true)]
    [TestCase(InteractType.Mesh, false)]
    public void InteractObjectRep_CountsTheListedInteractType(InteractType type, bool expected) {
        var condition = new ConditionMetadata(ConditionType.interact_object_rep, 1,
            null, new ConditionMetadata.Parameters(Integers: [2]));
        Assert.That(condition.Check(null!, targetLong: (long) type, codeLong: 10000001), Is.EqualTo(expected));
    }

    // A target range holds the values from Min to Max (it was tested the wrong way round, so only Min == Max matched).
    [TestCase(ConditionType.chat, 5, true)]
    [TestCase(ConditionType.chat, 1, true)]
    [TestCase(ConditionType.chat, 9, true)]
    [TestCase(ConditionType.chat, 10, false)]
    [TestCase(ConditionType.revise_achieve_single_grade, 5, true)]
    [TestCase(ConditionType.fish, 5, true)]
    [TestCase(ConditionType.fish, 0, false)]
    public void TargetRange_CountsTheValuesInside(ConditionType type, long value, bool expected) {
        var target = new ConditionMetadata.Parameters(Range: new ConditionMetadata.Range<int>(1, 9));
        Assert.That(target.CheckTarget(null!, type, longValue: value), Is.EqualTo(expected));
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

    // A completed daily quest counts for quest_daily when its id is one of the row's codes.
    [TestCase(30000359, true)]
    [TestCase(30000360, false)]
    public void QuestDaily_CountsTheListedQuests(int questId, bool expected) {
        var condition = new ConditionMetadata(ConditionType.quest_daily, 4,
            new ConditionMetadata.Parameters(Integers: [30000359]), null);
        Assert.That(condition.Check(null!, codeLong: questId), Is.EqualTo(expected));
    }

    // Only a daily quest (repeat 2) counts for quest_daily.
    [TestCase(2, true)]
    [TestCase(0, false)]
    [TestCase(1, false)]
    [TestCase(3, false)]
    public void QuestDaily_OnlyDailyQuests(int repeat, bool expected) {
        Assert.That(QuestRules.CountsAsDaily(Basic(QuestType.WorldQuest, repeat)), Is.EqualTo(expected));
    }

    // An opened box counts for openItemBox when its item id is one of the row's codes.
    [TestCase(20302745, true)]
    [TestCase(20302746, false)]
    public void OpenItemBox_CountsTheListedBox(int boxItemId, bool expected) {
        var condition = new ConditionMetadata(ConditionType.openItemBox, 200,
            new ConditionMetadata.Parameters(Integers: [20302745]), null);
        Assert.That(condition.Check(null!, codeLong: boxItemId), Is.EqualTo(expected));
    }

    // A completed daily mission counts for quest_guide, whatever the mission; other quests do not.
    [TestCase(QuestType.DailyMission, true)]
    [TestCase(QuestType.EpicQuest, false)]
    [TestCase(QuestType.FieldMission, false)]
    public void QuestGuide_OnlyDailyMissions(QuestType type, bool expected) {
        Assert.That(QuestRules.CountsAsGuide(Basic(type, 1)), Is.EqualTo(expected));
        var condition = new ConditionMetadata(ConditionType.quest_guide, 1, null, null);
        Assert.That(condition.Check(null!), Is.True);
    }

    private static QuestMetadataBasic Basic(QuestType type, int repeat) {
        return new QuestMetadataBasic(ChapterId: 0, Type: type, Account: 0, StandardLevel: 1, Forfeitable: false,
            EventTag: string.Empty, AutoStart: false, Disabled: false, UsePostbox: false, StartNpc: 0, CompleteNpc: 0,
            CompleteMaps: null, ProgressMaps: null, Repeat: repeat);
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
