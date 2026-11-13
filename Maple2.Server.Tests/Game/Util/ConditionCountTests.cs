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

    // npc_lasthit_time (21230022): Alpha Turtle's killing blow at 23:59 local time; another minute or NPC does not count.
    [TestCase(23000017, "23:59", true)]
    [TestCase(23000017, "23:58", false)]
    [TestCase(23000018, "23:59", false)]
    public void NpcLastHitTime_CountsTheListedMinute(int npcId, string time, bool expected) {
        var condition = new ConditionMetadata(ConditionType.npc_lasthit_time, 1,
            new ConditionMetadata.Parameters(Integers: [23000017]), new ConditionMetadata.Parameters(Strings: ["23:59"]));
        Assert.That(condition.Check(null!, targetString: time, codeLong: npcId), Is.EqualTo(expected));
    }

    // npc_timeattack (21250001): the dungeon boss inside 480 seconds of the run; later or another NPC does not count.
    [TestCase(23000100, 300, true)]
    [TestCase(23000100, 480, true)]
    [TestCase(23000100, 481, false)]
    [TestCase(23000055, 300, false)]
    public void NpcTimeAttack_CountsInsideTheTarget(int npcId, long seconds, bool expected) {
        var condition = new ConditionMetadata(ConditionType.npc_timeattack, 1,
            new ConditionMetadata.Parameters(Integers: [23000100]), new ConditionMetadata.Parameters(Integers: [480]));
        Assert.That(condition.Check(null!, targetLong: seconds, codeLong: npcId), Is.EqualTo(expected));
    }

    // Fusing pets counts for pet_enchant; a row with a code (90000795) names the fused pet's item id.
    [TestCase(61000002, true)]
    [TestCase(61000003, false)]
    public void PetEnchant_CountsTheListedPet(int petItemId, bool expected) {
        var withCode = new ConditionMetadata(ConditionType.pet_enchant, 1,
            new ConditionMetadata.Parameters(Integers: [61000002]), null);
        var noCode = new ConditionMetadata(ConditionType.pet_enchant, 3, null, null);
        Assert.That(withCode.Check(null!, codeLong: petItemId), Is.EqualTo(expected));
        Assert.That(noCode.Check(null!, codeLong: petItemId), Is.True);
    }

    // A fusion counts for lapenshard_upgrade_result when it reaches the target tier (21100060: tier 6).
    [TestCase(6, true)]
    [TestCase(7, false)]
    public void LapenshardUpgradeResult_CountsTheTierReached(int tier, bool expected) {
        var condition = new ConditionMetadata(ConditionType.lapenshard_upgrade_result, 1, null,
            new ConditionMetadata.Parameters(Integers: [6]));
        Assert.That(condition.Check(null!, targetLong: tier), Is.EqualTo(expected));
    }

    // Pet evolution trophies name a rarity: the one reached (pet_evolution_by_rank, 23100383 code 2) or the one of
    // the pet that gained the points (pet_evolution_point_by_rank, 23100387 code 1).
    [TestCase(ConditionType.pet_evolution_by_rank, 2, 2, true)]
    [TestCase(ConditionType.pet_evolution_by_rank, 2, 3, false)]
    [TestCase(ConditionType.pet_evolution_point_by_rank, 1, 1, true)]
    [TestCase(ConditionType.pet_evolution_point_by_rank, 1, 2, false)]
    public void PetEvolution_CountsTheListedRarity(ConditionType type, int code, int rarity, bool expected) {
        var condition = new ConditionMetadata(type, 1, new ConditionMetadata.Parameters(Integers: [code]), null);
        Assert.That(condition.Check(null!, codeLong: rarity), Is.EqualTo(expected));
    }

    // A pet new to the album counts for pet_collect at its grade (23100276: target 1, any grade) and for
    // pet_first_collect with its item (23100377: code 61100100).
    [TestCase(1, true)]
    [TestCase(4, true)]
    public void PetCollect_CountsAnyGradeFromTheTarget(int rarity, bool expected) {
        var condition = new ConditionMetadata(ConditionType.pet_collect, 1, null, new ConditionMetadata.Parameters(Integers: [1]));
        Assert.That(condition.Check(null!, targetLong: rarity), Is.EqualTo(expected));
    }

    [TestCase(61100100, true)]
    [TestCase(61100101, false)]
    public void PetFirstCollect_CountsTheListedPet(int petItemId, bool expected) {
        var condition = new ConditionMetadata(ConditionType.pet_first_collect, 1, new ConditionMetadata.Parameters(Integers: [61100100]), null);
        Assert.That(condition.Check(null!, codeLong: petItemId), Is.EqualTo(expected));
    }

    // The nurturing trophies name the cube pet's stage-1 npc (92000095 play, 92000118 eat, 92000096 growth).
    [TestCase(ConditionType.nurturing_play, 50400282, true)]
    [TestCase(ConditionType.nurturing_eat, 50400309, true)]
    [TestCase(ConditionType.nurturing_growth, 50400282, true)]
    [TestCase(ConditionType.nurturing_eat, 50400341, false)]
    public void Nurturing_CountsTheListedPet(ConditionType type, int npcId, bool expected) {
        int code = type == ConditionType.nurturing_eat ? 50400309 : 50400282;
        var condition = new ConditionMetadata(type, 1, new ConditionMetadata.Parameters(Integers: [code]), null);
        Assert.That(condition.Check(null!, codeLong: npcId), Is.EqualTo(expected));
    }

    // A received maid craft counts for maid_get_item with its product (23500009: code 12030049).
    [TestCase(12030049, true)]
    [TestCase(12000045, false)]
    public void MaidGetItem_CountsTheListedProduct(int productItemId, bool expected) {
        var condition = new ConditionMetadata(ConditionType.maid_get_item, 1, new ConditionMetadata.Parameters(Integers: [12030049]), null);
        Assert.That(condition.Check(null!, codeLong: productItemId), Is.EqualTo(expected));
    }

    // A hit on a vibrate object counts with its materialTag (70400059 "Hit a tree 20 times": code "wood").
    [TestCase("wood", true)]
    [TestCase("stone", false)]
    public void VibrateObject_CountsTheListedMaterial(string material, bool expected) {
        var condition = new ConditionMetadata(ConditionType.vibrate_object, 20,
            new ConditionMetadata.Parameters(Strings: ["wood"]), null);
        Assert.That(condition.Check(null!, codeString: material), Is.EqualTo(expected));
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

    // An npc kill target can be a range of maps (quest 71000065: 3000001-3999999).
    [TestCase(3000001, true)]
    [TestCase(3500000, true)]
    [TestCase(3999999, true)]
    [TestCase(2000085, false)]
    [TestCase(4000000, false)]
    public void Npc_CountsAKillOnAMapInTheTargetRange(long mapId, bool expected) {
        var condition = new ConditionMetadata(ConditionType.npc, 5,
            null, new ConditionMetadata.Parameters(Range: new ConditionMetadata.Range<int>(3000001, 3999999)));
        Assert.That(condition.Check(null!, targetLong: mapId, codeLong: 21000063), Is.EqualTo(expected));
    }

    // A listed npc kill map counts only that map (10001600: target 2000171), as the client finds the value in the id
    // list; a target of 0 is no target.
    [TestCase(2000171, 2000171, true)]
    [TestCase(2000171, 2000276, false)]
    [TestCase(2000171, 2000100, false)]
    [TestCase(0, 2000276, true)]
    public void Npc_CountsAKillOnTheListedMapOnly(int targetMap, long mapId, bool expected) {
        var condition = new ConditionMetadata(ConditionType.npc, 5,
            null, new ConditionMetadata.Parameters(Integers: [targetMap]));
        Assert.That(condition.Check(null!, targetLong: mapId, codeLong: 21000063), Is.EqualTo(expected));
    }

    // A captured pet counts for pet_catch_id when its item id is the code (94000006: capture 2 of 61000153).
    [TestCase(61000153, true)]
    [TestCase(61000158, false)]
    public void PetCatchId_CountsTheListedPet(int petId, bool expected) {
        var condition = new ConditionMetadata(ConditionType.pet_catch_id, 2, new ConditionMetadata.Parameters(Integers: [61000153]), null);
        Assert.That(condition.Check(null!, codeLong: petId), Is.EqualTo(expected));
    }

    // A captured pet counts for pet_catch_grade when its rarity is the code (70600034 "Capture a rare pet": 2); the
    // event rows without a code count any capture.
    [TestCase(2, true)]
    [TestCase(1, false)]
    public void PetCatchGrade_CountsTheListedRarity(int rarity, bool expected) {
        var condition = new ConditionMetadata(ConditionType.pet_catch_grade, 1, new ConditionMetadata.Parameters(Integers: [2]), null);
        Assert.That(condition.Check(null!, codeLong: rarity), Is.EqualTo(expected));
        Assert.That(new ConditionMetadata(ConditionType.pet_catch_grade, 15, null, null).Check(null!, codeLong: rarity), Is.True);
    }

    // A captured pet counts for pet_catch_category when its collection group is the code (70600036: 3 Beast).
    [TestCase(3, true)]
    [TestCase(4, false)]
    [TestCase(0, false)]
    public void PetCatchCategory_CountsTheListedGroup(int group, bool expected) {
        var condition = new ConditionMetadata(ConditionType.pet_catch_category, 1, new ConditionMetadata.Parameters(Integers: [3]), null);
        Assert.That(condition.Check(null!, codeLong: group), Is.EqualTo(expected));
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

    // A dungeon reward counts for dungeon_reward_group when the room's group type is the row's code
    // (70700006 code 1, 89000215 code 2, 70700061 code 9, 70700003 code 10).
    [TestCase(1, DungeonGroupType.normal, true)]
    [TestCase(1, DungeonGroupType.raid, false)]
    [TestCase(2, DungeonGroupType.raid, true)]
    [TestCase(9, DungeonGroupType.item, true)]
    [TestCase(10, DungeonGroupType.vip, true)]
    [TestCase(10, DungeonGroupType.item, false)]
    public void DungeonRewardGroup_CountsTheListedGroup(int code, DungeonGroupType groupType, bool expected) {
        var condition = new ConditionMetadata(ConditionType.dungeon_reward_group, 1,
            new ConditionMetadata.Parameters(Integers: [code]), null);
        Assert.That(condition.Check(null!, codeLong: (long) groupType), Is.EqualTo(expected));
    }

    // A clear of a ranked room counts for dungeon_rank_clear when the room is the row's code
    // (93000174 "Clear the "Duel on the Deck" Fortress Rumble": 24091005).
    [TestCase(24091005, true)]
    [TestCase(24092005, false)]
    public void DungeonRankClear_CountsTheListedRoom(int dungeonId, bool expected) {
        var condition = new ConditionMetadata(ConditionType.dungeon_rank_clear, 1,
            new ConditionMetadata.Parameters(Integers: [24091005]), null);
        Assert.That(condition.Check(null!, codeLong: dungeonId), Is.EqualTo(expected));
    }

    // A ranked clear counts for dungeon_rank in the row's room when its rank (0 F to 5 S+) reaches the target
    // (22210045 "Earn A rank" 3; 93000173 "Spar with" the captain 0, any rank).
    [TestCase(24091005, DungeonMissionRank.A, true)]
    [TestCase(24091005, DungeonMissionRank.SPlus, true)]
    [TestCase(24091005, DungeonMissionRank.B, false)]
    [TestCase(24092005, DungeonMissionRank.SPlus, false)]
    public void DungeonRank_CountsTheRankReached(int dungeonId, DungeonMissionRank rank, bool expected) {
        var condition = new ConditionMetadata(ConditionType.dungeon_rank, 1,
            new ConditionMetadata.Parameters(Integers: [24091005]), new ConditionMetadata.Parameters(Integers: [3]));
        Assert.That(condition.Check(null!, targetLong: (long) rank, codeLong: dungeonId), Is.EqualTo(expected));
    }

    // A moved item counts for item_move only on the row's box: the target is a box id, so a higher box does not
    // count (93000061 "World Stone: Shadow Gate": 30001007 on box 905; boxes 906 and 907 belong to other rows).
    [TestCase(30001007, 905, true)]
    [TestCase(30001007, 906, false)]
    [TestCase(30001007, 904, false)]
    [TestCase(30001008, 905, false)]
    public void ItemMove_CountsOnlyTheListedBox(int itemId, int boxId, bool expected) {
        var condition = new ConditionMetadata(ConditionType.item_move, 1,
            new ConditionMetadata.Parameters(Integers: [30001007]), new ConditionMetadata.Parameters(Integers: [905]));
        Assert.That(condition.Check(null!, targetLong: boxId, codeLong: itemId), Is.EqualTo(expected));
    }

    // A design made from a template counts for item_design with its kind (70400058 "Craft 1 template in the
    // workshop": item, furniture and mount).
    [TestCase(UgcType.Item, true)]
    [TestCase(UgcType.Mount, true)]
    [TestCase(UgcType.Banner, false)]
    public void ItemDesign_CountsTheListedKinds(UgcType type, bool expected) {
        var condition = new ConditionMetadata(ConditionType.item_design, 1,
            new ConditionMetadata.Parameters(Integers: [1, 2, 7]), null);
        Assert.That(condition.Check(null!, codeLong: (long) type), Is.EqualTo(expected));
    }

    // A hit by a monster's skill counts for skill_damage_npc when the skill id is one of the row's codes
    // (21233021 "Get sent flying by $npc:23001001$": skills 49905001 and 49905002).
    [TestCase(49905002, true)]
    [TestCase(49905003, false)]
    public void SkillDamageNpc_CountsTheListedSkills(int skillId, bool expected) {
        var condition = new ConditionMetadata(ConditionType.skill_damage_npc, 1,
            new ConditionMetadata.Parameters(Integers: [49905001, 49905002]), null);
        Assert.That(condition.Check(null!, codeLong: skillId), Is.EqualTo(expected));
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

    // An alliance mission counts for quest_alliance with its alliance as the code (93000188 "Maple Alliance Drill":
    // code 2, complete a Maple Alliance mission); a field quest or a quest without an alliance does not.
    [TestCase(QuestType.AllianceQuest, AllianceType.MapleAlliance, true)]
    [TestCase(QuestType.AllianceQuest, AllianceType.DarkWind, false)]
    [TestCase(QuestType.FieldQuest, AllianceType.MapleAlliance, false)]
    [TestCase(QuestType.AllianceQuest, AllianceType.AnyFaction, false)]
    public void QuestAlliance_CountsAMissionOfTheListedAlliance(QuestType type, AllianceType alliance, bool expected) {
        QuestMetadataBasic basic = Basic(type, 3) with { Alliance = alliance };
        var condition = new ConditionMetadata(ConditionType.quest_alliance, 1, new ConditionMetadata.Parameters(Integers: [2]), null);
        bool counted = QuestRules.CountsAsAllianceMission(basic) && condition.Check(null!, codeLong: (long) basic.Alliance);
        Assert.That(counted, Is.EqualTo(expected));
    }

    // An alliance field quest counts for quest_field with its alliance as the code (93000159 "Danger Unknown": code 2,
    // complete a Maple Alliance field mission); an alliance mission does not.
    [TestCase(QuestType.FieldQuest, AllianceType.MapleAlliance, true)]
    [TestCase(QuestType.FieldQuest, AllianceType.GreenHoods, false)]
    [TestCase(QuestType.AllianceQuest, AllianceType.MapleAlliance, false)]
    [TestCase(QuestType.FieldQuest, AllianceType.AnyFaction, false)]
    public void QuestField_CountsAFieldMissionOfTheListedAlliance(QuestType type, AllianceType alliance, bool expected) {
        QuestMetadataBasic basic = Basic(type, 2) with { Alliance = alliance };
        var condition = new ConditionMetadata(ConditionType.quest_field, 1, new ConditionMetadata.Parameters(Integers: [2]), null);
        bool counted = QuestRules.CountsAsAllianceFieldMission(basic) && condition.Check(null!, codeLong: (long) basic.Alliance);
        Assert.That(counted, Is.EqualTo(expected));
    }

    // The rank index the ingest stores (S 0 to D 4, none 5) maps back to the quest XML's letter, the code of the
    // quest_alliance_by_grade rows; a quest without a rank, a field quest or one without an alliance gives none.
    [TestCase(QuestType.AllianceQuest, AllianceType.GreenHoods, 0, "S")]
    [TestCase(QuestType.AllianceQuest, AllianceType.GreenHoods, 1, "A")]
    [TestCase(QuestType.AllianceQuest, AllianceType.GreenHoods, 2, "B")]
    [TestCase(QuestType.AllianceQuest, AllianceType.GreenHoods, 3, "C")]
    [TestCase(QuestType.AllianceQuest, AllianceType.GreenHoods, 4, "D")]
    [TestCase(QuestType.AllianceQuest, AllianceType.GreenHoods, 5, null)]
    [TestCase(QuestType.FieldQuest, AllianceType.GreenHoods, 4, null)]
    [TestCase(QuestType.AllianceQuest, AllianceType.AnyFaction, 4, null)]
    public void QuestAllianceByGrade_RankLetter(QuestType type, AllianceType alliance, int rank, string? expected) {
        QuestMetadataBasic basic = Basic(type, 1) with { Alliance = alliance, Rank = rank };
        Assert.That(QuestRules.AllianceRankLetter(basic), Is.EqualTo(expected));
    }

    // The code is the letter and the target the alliance (22500034: code D, target 1 to 5).
    [TestCase("D", 5, true)]
    [TestCase("C", 5, false)]
    [TestCase("D", 9, false)]
    public void QuestAllianceByGrade_CodeLetterTargetAlliance(string letter, int alliance, bool expected) {
        var condition = new ConditionMetadata(ConditionType.quest_alliance_by_grade, 1,
            new ConditionMetadata.Parameters(Strings: ["D"]), new ConditionMetadata.Parameters(Integers: [1, 2, 3, 4, 5]));
        Assert.That(condition.Check(null!, codeString: letter, targetLong: alliance), Is.EqualTo(expected));
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

    // The played quests of these types list no code or target, so every event counts.
    [TestCase(ConditionType.item_break)] // 74000011 "Dismantle 1 item"
    [TestCase(ConditionType.guild_join)] // 70300010 "Join a guild"
    [TestCase(ConditionType.pet_enchant_exp)] // 70600033 "Acquire 10,000 pet experience through fusion"
    [TestCase(ConditionType.guild_attendance)] // 70300011 "Guild Check-In"
    [TestCase(ConditionType.dungeon_reward)] // 70700002 "Collect a reward from any dungeon"
    [TestCase(ConditionType.dungeon_help_beginner_helpee)] // 70210090 "Complete a dungeon as a dungeon rookie with the aid of a dungeon veteran"
    public void CodelessQuestRow_CountsEveryEvent(ConditionType type) {
        var condition = new ConditionMetadata(type, 1, null, null);
        Assert.That(condition.Check(null!), Is.True);
    }

    // dungeon_reward rows that name a dungeon (21230122 "Green Lapenta Eruption": code 27000) count a reward from any
    // of its rooms (room id / 1000, the client's GetDungeonIdOfRoom), not one from another dungeon's room.
    [TestCase(27000001L, true)]
    [TestCase(27000002L, true)]
    [TestCase(27001001L, false)]
    [TestCase(23021003L, false)]
    public void DungeonReward_CountsTheRoomsOfTheNamedDungeon(long roomId, bool expected) {
        var condition = new ConditionMetadata(ConditionType.dungeon_reward, 15, new ConditionMetadata.Parameters(Integers: [27000]), null);
        Assert.That(condition.Check(null!, codeLong: roomId), Is.EqualTo(expected));
    }

    [Test]
    public void EnterOtherHouse_ConditionWithoutCodesCounts() {
        var condition = new ConditionMetadata(ConditionType.enter_otherhouse, 3, null, null);
        Assert.That(condition.Check(null!), Is.True);
    }
}
