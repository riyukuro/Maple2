using M2dXmlGenerator;
using Maple2.Database.Extensions;
using Maple2.File.Ingest.Helpers;
using Maple2.File.Ingest.Utils;
using Maple2.File.IO;
using Maple2.File.Parser;
using Maple2.File.Parser.Enum;
using Maple2.File.Parser.Flat.Convert;
using Maple2.File.Parser.Tools;
using Maple2.File.Parser.Xml.Table;
using Maple2.File.Parser.Xml.Table.Server;
using Maple2.Model;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Game.Shop;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Newtonsoft.Json.Linq;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Xml;
using System.Xml.Serialization;
using DayOfWeek = System.DayOfWeek;
using ExpType = Maple2.Model.Enum.ExpType;
using Fish = Maple2.File.Parser.Xml.Table.Server.Fish;
using FishingSpot = Maple2.File.Parser.Xml.Table.Server.FishingSpot;
using GuildNpcType = Maple2.Model.Enum.GuildNpcType;
using IndividualItemDrop = Maple2.File.Parser.Xml.Table.Server.IndividualItemDrop;
using InstanceType = Maple2.Model.Enum.InstanceType;
using JobConditionTable = Maple2.Model.Metadata.JobConditionTable;
using MergeOption = Maple2.File.Parser.Xml.Table.Server.MergeOption;
using ScriptEventType = Maple2.Model.Enum.ScriptEventType;
using ScriptType = Maple2.Model.Enum.ScriptType;
using TimeEventType = Maple2.File.Parser.Enum.TimeEventType;

namespace Maple2.File.Ingest.Mapper;

public class ServerTableMapper : TypeMapper<ServerTableMetadata> {
    private readonly ServerTableParser parser;
    // The client's Xml.m2d: its table/constants.xml holds the prices the client shows.
    private readonly M2dReader? clientReader;
    // The Xml pack of the client that is played, when it is not the data folder's (MS2_PLAYED_CLIENT_XML).
    private readonly M2dReader? playedReader;

    // The server pack, for the attributes the parser does not read.
    private readonly M2dReader xmlReader;

    // The drop table file (ServerDropTable): today's, or the original with MS2_ORIGINAL_DROP_TABLE.
    private readonly string dropFile;
    // MS2_REPLAY_ATTEND_GIFT (AttendGiftReplay): one ended attendance gift event is kept, moved to the ingest day.
    private readonly bool replayAttendGift;
    // Resource/Exported.m2d: the maps' xblock files (the Hide and Seek props).
    private readonly M2dReader? exportedReader;
    // MS2_SERVER_OVERLAY (ServerOverlay): files read instead of the pack's, for the tables that take one.
    private readonly ServerOverlay overlay;

    public ServerTableMapper(M2dReader xmlReader, M2dReader? clientReader = null, M2dReader? playedReader = null, bool originalDropTable = false, bool replayAttendGift = false, M2dReader? exportedReader = null, ServerOverlay? overlay = null) {
        this.xmlReader = xmlReader;
        this.overlay = overlay ?? ServerOverlay.None;
        this.exportedReader = exportedReader;
        this.replayAttendGift = replayAttendGift;
        dropFile = ServerDropTable.FileName(originalDropTable);
        this.playedReader = playedReader;
        parser = new ServerTableParser(xmlReader);
        this.clientReader = clientReader;
    }

    protected override IEnumerable<ServerTableMetadata> Map() {
        yield return new ServerTableMetadata {
            Name = ServerTableNames.INSTANCE_FIELD,
            Table = ParseInstanceField(),
        };
        yield return new ServerTableMetadata {
            Name = ServerTableNames.SCRIPT_CONDITION,
            Table = ParseScriptCondition(),
        };
        yield return new ServerTableMetadata {
            Name = ServerTableNames.SCRIPT_FUNCTION,
            Table = ParseScriptFunction(),
        };
        yield return new ServerTableMetadata {
            Name = ServerTableNames.SCRIPT_EVENT,
            Table = ParseScriptEventConditionTable(),
        };
        yield return new ServerTableMetadata {
            Name = ServerTableNames.JOB_CONDITION,
            Table = ParseJobCondition(),
        };
        yield return new ServerTableMetadata {
            Name = ServerTableNames.BONUS_GAME,
            Table = ParseBonusGameTable(),
        };
        yield return new ServerTableMetadata {
            Name = ServerTableNames.GLOBAL_DROP_ITEM_BOX,
            Table = ParseGlobalItemDropTable(),
        };
        yield return new ServerTableMetadata {
            Name = ServerTableNames.USER_STAT,
            Table = ParseUserStat(),
        };
        yield return new ServerTableMetadata {
            Name = ServerTableNames.INDIVIDUAL_DROP_ITEM,
            Table = ParseIndividualItemDropTable(),
        };
        yield return new ServerTableMetadata {
            Name = ServerTableNames.PRESTIGE_EXP,
            Table = ParsePrestigeExpTable(),
        };
        yield return new ServerTableMetadata {
            Name = ServerTableNames.PRESTIGE_ID_EXP,
            Table = ParsePrestigeIdExpTable(),
        };
        yield return new ServerTableMetadata {
            Name = ServerTableNames.TIME_EVENT,
            Table = ParseTimeEventTable(),
        };
        yield return new ServerTableMetadata {
            Name = ServerTableNames.GAME_EVENT,
            Table = ParseGameEventTable(),
        };
        yield return new ServerTableMetadata {
            Name = ServerTableNames.OX_QUIZ,
            Table = ParseOxQuizTable(),
        };
        yield return new ServerTableMetadata {
            Name = ServerTableNames.ITEM_MERGE,
            Table = ParseItemMergeOptionTable(),
        };
        yield return new ServerTableMetadata {
            Name = ServerTableNames.SHOP,
            Table = ParseShop(),
        };
        yield return new ServerTableMetadata {
            Name = ServerTableNames.SHOP_ITEM,
            Table = ParseShopItems(),
        };
        yield return new ServerTableMetadata {
            Name = ServerTableNames.BEAUTY_SHOP,
            Table = ParseBeautyShops(),
        };
        yield return new ServerTableMetadata {
            Name = ServerTableNames.MERET_MARKET,
            Table = ParseMeretCustomShop(),
        };
        yield return new ServerTableMetadata {
            Name = ServerTableNames.FISH,
            Table = ParseFish(),
        };
        yield return new ServerTableMetadata {
            Name = ServerTableNames.COMBINE_SPAWN,
            Table = ParseCombineSpawn(),
        };
        yield return new ServerTableMetadata {
            Name = ServerTableNames.ENCHANT_OPTION,
            Table = ParseEnchantOption(),
        };
        yield return new ServerTableMetadata {
            Name = ServerTableNames.UNLIMITED_ENCHANT_OPTION,
            Table = ParseUnlimitedEnchantOption(),
        };
        yield return new ServerTableMetadata {
            Name = ServerTableNames.CONSTANTS,
            Table = ParseConstants(),
        };
        yield return new ServerTableMetadata {
            Name = ServerTableNames.ROOM_RANDOM,
            Table = ParseRoomRandom(),
        };
        yield return new ServerTableMetadata {
            Name = ServerTableNames.NPC_STAT_FACTOR,
            Table = ParseNpcStatFactor(),
        };
        yield return new ServerTableMetadata {
            Name = ServerTableNames.MONSTER_EXP,
            Table = ParseMonsterExp(),
        };
        yield return new ServerTableMetadata {
            Name = ServerTableNames.ARCADE_REWARD,
            Table = ParseArcadeReward(),
        };
        yield return new ServerTableMetadata {
            Name = ServerTableNames.HIDE_AND_SEEK,
            Table = ParseHideAndSeek(),
        };
        yield return new ServerTableMetadata {
            Name = ServerTableNames.TERM_TAG,
            Table = ParseTermTag(),
        };
        yield return new ServerTableMetadata {
            Name = ServerTableNames.FEATURE,
            Table = MapFeatures(FeatureLocaleFilter.Features),
        };
        yield return new ServerTableMetadata {
            Name = ServerTableNames.WORLD_CHAMPION,
            Table = ParseWorldChampion(),
        };
    }

    // The features the rows were filtered by (FeatureLevels: the data folder's tables, or the played client's), in name
    // order so the same features give the same row.
    public static FeatureTable MapFeatures(IReadOnlyDictionary<string, int> enabled) {
        var features = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach ((string name, int level) in enabled) {
            features[name] = level;
        }
        return new FeatureTable(features);
    }

    private InstanceFieldTable ParseInstanceField() {
        var results = new Dictionary<int, InstanceFieldMetadata>();
        foreach ((int instanceId, InstanceField instanceField) in parser.ParseInstanceField()) {
            foreach (int fieldId in instanceField.fieldIDs) {

                InstanceFieldMetadata instanceFieldMetadata = new(
                    MapId: fieldId,
                    Type: Enum.TryParse(instanceField.instanceType.ToString(), out InstanceType instanceType) ? instanceType : InstanceType.none,
                    InstanceId: instanceId,
                    BackupSourcePortal: instanceField.backupSourcePortal,
                    PoolCount: instanceField.poolCount,
                    SaveField: instanceField.isSaveField,
                    NpcStatFactorId: instanceField.npcStatFactorID,
                    MaxCount: instanceField.maxCount,
                    OpenType: instanceField.openType,
                    OpenValue: instanceField.openValue
                );

                results.Add(fieldId, instanceFieldMetadata);
            }
        }

        return new InstanceFieldTable(results);
    }

    private ScriptConditionTable ParseScriptCondition() {
        var results = new Dictionary<int, Dictionary<int, ScriptConditionMetadata>>();
        results = MergeNpcScriptConditions(results, parser.ParseNpcScriptCondition());
        results = MergeQuestScriptConditions(results, parser.ParseQuestScriptCondition());

        return new ScriptConditionTable(results);
    }

    private Dictionary<int, Dictionary<int, ScriptConditionMetadata>> MergeNpcScriptConditions(Dictionary<int, Dictionary<int, ScriptConditionMetadata>> results, IEnumerable<(int NpcId, IDictionary<int, NpcScriptCondition> ScriptConditions)> parser) {
        Dictionary<(int NpcId, int ScriptId), (int Received, int Complete)> rewardDungeons = NpcScriptRewardDungeons();
        foreach ((int npcId, IDictionary<int, NpcScriptCondition> scripts) in parser) {
            var scriptConditions = new Dictionary<int, ScriptConditionMetadata>();
            foreach ((int scriptId, NpcScriptCondition scriptCondition) in scripts) {
                var questStarted = new Dictionary<int, bool>();
                foreach (string quest in scriptCondition.quest_start) {
                    KeyValuePair<int, bool> parsedQuest = ParseToIntKeyValuePair(quest);
                    questStarted.Add(parsedQuest.Key, parsedQuest.Value);
                }

                var questsCompleted = new Dictionary<int, bool>();
                foreach (string quest in scriptCondition.quest_complete) {
                    KeyValuePair<int, bool> parsedQuest = ParseToIntKeyValuePair(quest);
                    questsCompleted.Add(parsedQuest.Key, parsedQuest.Value);
                }

                var items = new List<KeyValuePair<ItemComponent, bool>>();
                for (int i = 0; i < scriptCondition.item.Length; i++) {
                    KeyValuePair<int, bool> parsedItem = ParseToIntKeyValuePair(scriptCondition.item[i]);
                    string itemCount = scriptCondition.itemCount.ElementAtOrDefault(i) ?? "1";
                    if (!int.TryParse(itemCount, out int itemAmount)) {
                        itemAmount = 1;
                    }
                    var item = new ItemComponent(parsedItem.Key, -1, itemAmount, ItemTag.None);
                    items.Add(new KeyValuePair<ItemComponent, bool>(item, parsedItem.Value));
                }

                scriptConditions.Add(scriptId, new ScriptConditionMetadata(
                    Id: npcId,
                    ScriptId: scriptId,
                    Type: ScriptType.Npc,
                    Maid: new ScriptConditionMetadata.MaidData(
                        Authority: scriptCondition.maid_auth,
                        Expired: MaidRules.ConditionFlag(scriptCondition.maid_expired),
                        ReadyToPay: MaidRules.ConditionFlag(scriptCondition.maid_ready_to_pay),
                        ClosenessRank: scriptCondition.maid_affinity_grade,
                        ClosenessTime: ParseToIntKeyValuePair(scriptCondition.maid_affinity_time),
                        MoodTime: ParseToIntKeyValuePair(scriptCondition.maid_mood_time),
                        DaysBeforeExpired: ParseToIntKeyValuePair(scriptCondition.maid_day_before_expired)
                    ),
                    Wedding: new ScriptConditionMetadata.WeddingData(
                        HasReservation: scriptCondition.weddingHallBooking < 0 ? null : scriptCondition.weddingHallBooking == 1,
                        MarriageDays: scriptCondition.marriageDate,
                        UserState: scriptCondition.weddingState < 0 ? null : (MaritalStatus) scriptCondition.weddingState,
                        HallState: ParseToStringKeyValuePair(scriptCondition.weddingHallState),
                        CoolingOff: scriptCondition.coolingOff,
                        HallEntryType: ParseToStringKeyValuePair(scriptCondition.weddingHallEntryType ?? string.Empty)),
                    JobCode: scriptCondition.job?.Select(job => (JobCode) job).ToList() ?? [],
                    QuestStarted: questStarted,
                    QuestCompleted: questsCompleted,
                    Items: items,
                    Buff: ParseToIntKeyValuePair(scriptCondition.buff),
                    Meso: ParseToIntKeyValuePair(scriptCondition.meso),
                    Level: ParseToIntKeyValuePair(scriptCondition.level),
                    AchieveCompleted: ParseToIntKeyValuePair(scriptCondition.achieve_complete),
                    DeathPenalty: scriptCondition.panelty == 1,
                    InGuild: scriptCondition.guild,
                    ReceivedRewardDungeonId: rewardDungeons.GetValueOrDefault((npcId, scriptId)).Received,
                    CompleteRewardDungeonId: rewardDungeons.GetValueOrDefault((npcId, scriptId)).Complete
                ));
            }
            results.Add(npcId, scriptConditions);
        }
        return results;
    }

    // The parser's NpcScriptCondition has no receivedRewardDungeonID or completeRewardDungeonID; they are
    // read from the table's rows here (the colosseum npc's continue-play choice).
    private Dictionary<(int NpcId, int ScriptId), (int Received, int Complete)> NpcScriptRewardDungeons() {
        Maple2.File.IO.Crypto.Common.PackFileEntry? entry = xmlReader.Files.FirstOrDefault(file =>
            file.Name.Equals("table/Server/npcScriptCondition_Final.xml", StringComparison.OrdinalIgnoreCase));
        return entry == null ? [] : NpcScriptRewardDungeons(xmlReader.GetXmlDocument(entry));
    }

    public static Dictionary<(int NpcId, int ScriptId), (int Received, int Complete)> NpcScriptRewardDungeons(XmlDocument document) {
        var results = new Dictionary<(int, int), (int, int)>();
        foreach (XmlNode node in document.SelectNodes("ms2/condition")!) {
            int.TryParse(node.Attributes?["receivedRewardDungeonID"]?.Value, out int received);
            int.TryParse(node.Attributes?["completeRewardDungeonID"]?.Value, out int complete);
            if ((received == 0 && complete == 0)
                || !int.TryParse(node.Attributes?["npcID"]?.Value, out int npcId)
                || !int.TryParse(node.Attributes?["scriptID"]?.Value, out int scriptId)) {
                continue;
            }
            results[(npcId, scriptId)] = (received, complete);
        }
        return results;
    }

    private Dictionary<int, Dictionary<int, ScriptConditionMetadata>> MergeQuestScriptConditions(Dictionary<int, Dictionary<int, ScriptConditionMetadata>> results, IEnumerable<(int NpcId, IDictionary<int, QuestScriptCondition> ScriptConditions)> parser) {
        foreach ((int questId, IDictionary<int, QuestScriptCondition> scripts) in parser) {
            var scriptConditions = new Dictionary<int, ScriptConditionMetadata>();
            foreach ((int scriptId, QuestScriptCondition scriptCondition) in scripts) {
                var questStarted = new Dictionary<int, bool>();
                foreach (string quest in scriptCondition.quest_start) {
                    KeyValuePair<int, bool> parsedQuest = ParseToIntKeyValuePair(quest);
                    questStarted.Add(parsedQuest.Key, parsedQuest.Value);
                }

                var questsCompleted = new Dictionary<int, bool>();
                foreach (string quest in scriptCondition.quest_complete) {
                    KeyValuePair<int, bool> parsedQuest = ParseToIntKeyValuePair(quest);
                    questsCompleted.Add(parsedQuest.Key, parsedQuest.Value);
                }

                var items = new List<KeyValuePair<ItemComponent, bool>>();
                for (int i = 0; i < scriptCondition.item.Length; i++) {
                    KeyValuePair<int, bool> parsedItem = ParseToIntKeyValuePair(scriptCondition.item[i]);
                    string itemCount = scriptCondition.itemCount.ElementAtOrDefault(i) ?? "1";
                    if (!int.TryParse(itemCount, out int itemAmount)) {
                        itemAmount = 1;
                    }
                    var item = new ItemComponent(parsedItem.Key, -1, itemAmount, ItemTag.None);
                    items.Add(new KeyValuePair<ItemComponent, bool>(item, parsedItem.Value));
                }

                scriptConditions.Add(scriptId, new ScriptConditionMetadata(
                    Id: questId,
                    ScriptId: scriptId,
                    Type: ScriptType.Quest,
                    Maid: new ScriptConditionMetadata.MaidData(
                        Authority: scriptCondition.maid_auth,
                        Expired: MaidRules.ConditionFlag(scriptCondition.maid_expired),
                        ReadyToPay: MaidRules.ConditionFlag(scriptCondition.maid_ready_to_pay),
                        ClosenessRank: scriptCondition.maid_affinity_grade,
                        ClosenessTime: ParseToIntKeyValuePair(scriptCondition.maid_affinity_time),
                        MoodTime: ParseToIntKeyValuePair(scriptCondition.maid_mood_time),
                        DaysBeforeExpired: ParseToIntKeyValuePair(scriptCondition.maid_day_before_expired)
                    ),
                    Wedding: new ScriptConditionMetadata.WeddingData(
                        HasReservation: scriptCondition.weddingHallBooking < 0 ? null : scriptCondition.weddingHallBooking == 1,
                        MarriageDays: scriptCondition.marriageDate,
                        UserState: scriptCondition.weddingState < 0 ? null : (MaritalStatus) scriptCondition.weddingState,
                        HallState: ParseToStringKeyValuePair(scriptCondition.weddingHallState),
                        CoolingOff: scriptCondition.coolingOff,
                        HallEntryType: ParseToStringKeyValuePair(scriptCondition.weddingHallEntryType ?? string.Empty)),
                    JobCode: scriptCondition.job?.Select(job => (JobCode) job).ToList() ?? [],
                    QuestStarted: questStarted,
                    QuestCompleted: questsCompleted,
                    Items: items,
                    Buff: ParseToIntKeyValuePair(scriptCondition.buff),
                    Meso: ParseToIntKeyValuePair(scriptCondition.meso),
                    Level: ParseToIntKeyValuePair(scriptCondition.level),
                    AchieveCompleted: ParseToIntKeyValuePair(scriptCondition.achieve_complete),
                    InGuild: scriptCondition.guild,
                    DeathPenalty: scriptCondition.panelty == 1
                ));
            }
            results.Add(questId, scriptConditions);
        }
        return results;
    }

    private static KeyValuePair<int, bool> ParseToIntKeyValuePair(string input) {
        bool value = !input.StartsWith("!");

        if (!value) {
            input = input.Replace("!", "");
        }

        if (!int.TryParse(input, out int key)) {
            key = 0;
        }
        return new KeyValuePair<int, bool>(key, value);
    }

    private static KeyValuePair<string, bool> ParseToStringKeyValuePair(string input) {
        bool value = !input.StartsWith("!");

        if (!value) {
            input = input.Substring(1);
        }
        return new KeyValuePair<string, bool>(input, value);
    }

    private ScriptFunctionTable ParseScriptFunction() {
        var results = new Dictionary<int, Dictionary<int, Dictionary<int, ScriptFunctionMetadata>>>();
        results = MergeNpcScriptFunctions(results, parser.ParseNpcScriptFunction());
        results = MergeQuestScriptFunctions(results, parser.ParseQuestScriptFunction());

        return new ScriptFunctionTable(results);
    }

    private static Dictionary<int, Dictionary<int, Dictionary<int, ScriptFunctionMetadata>>> MergeNpcScriptFunctions(Dictionary<int, Dictionary<int, Dictionary<int, ScriptFunctionMetadata>>> results, IEnumerable<(int NpcId, IDictionary<int, NpcScriptFunction> ScriptFunctions)> parser) {
        foreach ((int npcId, IDictionary<int, NpcScriptFunction> scripts) in parser) {
            var scriptDict = new Dictionary<int, Dictionary<int, ScriptFunctionMetadata>>(); // scriptIds, functionDict
            foreach ((int scriptId, NpcScriptFunction scriptFunction) in scripts) {
                var presentItems = new List<ItemComponent>();
                for (int i = 0; i < scriptFunction.presentItemID.Length; i++) {
                    short itemRarity = scriptFunction.presentItemRank.ElementAtOrDefault(i) != default(short) ? scriptFunction.presentItemRank.ElementAtOrDefault(i) : (short) -1;
                    int itemAmount = scriptFunction.presentItemAmount.ElementAtOrDefault(i) != default ? scriptFunction.presentItemAmount.ElementAtOrDefault(i) : 1;
                    presentItems.Add(new ItemComponent(scriptFunction.presentItemID[i], itemRarity, itemAmount, ItemTag.None));
                }

                var collectItems = new List<ItemComponent>();
                for (int i = 0; i < scriptFunction.collectItemID.Length; i++) {
                    int itemAmount = scriptFunction.collectItemAmount.ElementAtOrDefault(i) != default ? scriptFunction.collectItemAmount.ElementAtOrDefault(i) : 1;
                    collectItems.Add(new ItemComponent(scriptFunction.collectItemID[i], -1, itemAmount, ItemTag.None));
                }

                var metadata = new ScriptFunctionMetadata(
                    Id: npcId, // NpcId or QuestId
                    ScriptId: scriptId,
                    Type: ScriptType.Npc,
                    FunctionId: scriptFunction.functionID,
                    EndFunction: scriptFunction.endFunction,
                    PortalId: scriptFunction.portal,
                    UiName: scriptFunction.uiName,
                    UiArg: scriptFunction.uiArg,
                    UiArg2: scriptFunction.uiArg2,
                    MoveMapId: scriptFunction.moveFieldID,
                    MovePortalId: scriptFunction.moveFieldPortalID,
                    MoveMapMovie: scriptFunction.moveFieldMovie,
                    Emoticon: scriptFunction.emoticon,
                    PresentItems: presentItems,
                    CollectItems: collectItems,
                    SetTriggerValueTriggerId: scriptFunction.setTriggerValueTriggerID,
                    SetTriggerValueKey: scriptFunction.setTriggerValueKey,
                    SetTriggerValue: scriptFunction.setTriggerValue,
                    Divorce: scriptFunction.divorce,
                    PresentExp: scriptFunction.presentExp,
                    CollectMeso: scriptFunction.collectMeso,
                    MaidMoodIncrease: scriptFunction.maidMoodUp,
                    MaidClosenessIncrease: scriptFunction.maidAffinityUp,
                    MaidPay: scriptFunction.maidPay
                );
                if (!scriptDict.TryGetValue(scriptId, out Dictionary<int, ScriptFunctionMetadata>? functionDict)) {
                    functionDict = new Dictionary<int, ScriptFunctionMetadata> {
                        { scriptFunction.functionID, metadata },
                    };
                    scriptDict.Add(scriptId, functionDict);
                } else {
                    functionDict.Add(scriptFunction.functionID, metadata);
                }
            }
            results.Add(npcId, scriptDict);
        }
        return results;
    }

    public static Dictionary<int, Dictionary<int, Dictionary<int, ScriptFunctionMetadata>>> MergeQuestScriptFunctions(Dictionary<int, Dictionary<int, Dictionary<int, ScriptFunctionMetadata>>> results, IEnumerable<(int NpcId, IDictionary<int, QuestScriptFunction> ScriptFunctions)> parser) {
        foreach ((int questId, IDictionary<int, QuestScriptFunction> scripts) in parser) {
            var scriptDict = new Dictionary<int, Dictionary<int, ScriptFunctionMetadata>>(); // scriptIds, functionDict
            foreach ((int scriptId, QuestScriptFunction scriptFunction) in scripts) {
                var presentItems = new List<ItemComponent>();
                for (int i = 0; i < scriptFunction.presentItemID.Length; i++) {
                    short itemRarity = scriptFunction.presentItemRank.ElementAtOrDefault(i) != default(short) ? scriptFunction.presentItemRank.ElementAtOrDefault(i) : (short) -1;
                    int itemAmount = scriptFunction.presentItemAmount.ElementAtOrDefault(i) != default ? scriptFunction.presentItemAmount.ElementAtOrDefault(i) : 1;
                    presentItems.Add(new ItemComponent(scriptFunction.presentItemID[i], itemRarity, itemAmount, ItemTag.None));
                }

                var collectItems = new List<ItemComponent>();
                for (int i = 0; i < scriptFunction.collectItemID.Length; i++) {
                    int itemAmount = scriptFunction.collectItemAmount.ElementAtOrDefault(i) != default ? scriptFunction.collectItemAmount.ElementAtOrDefault(i) : 1;
                    collectItems.Add(new ItemComponent(scriptFunction.collectItemID[i], -1, itemAmount, ItemTag.None));
                }

                var metadata = new ScriptFunctionMetadata(
                    Id: questId,
                    ScriptId: scriptId,
                    Type: ScriptType.Quest,
                    FunctionId: scriptFunction.functionID,
                    EndFunction: scriptFunction.endFunction,
                    PortalId: scriptFunction.portal,
                    UiName: scriptFunction.uiName,
                    UiArg: scriptFunction.uiArg,
                    UiArg2: scriptFunction.uiArg2,
                    MoveMapId: scriptFunction.moveFieldID,
                    MovePortalId: scriptFunction.moveFieldPortalID,
                    MoveMapMovie: scriptFunction.moveFieldMovie,
                    Emoticon: scriptFunction.emoticon,
                    PresentItems: presentItems,
                    CollectItems: collectItems,
                    SetTriggerValueTriggerId: scriptFunction.setTriggerValueTriggerID,
                    SetTriggerValueKey: scriptFunction.setTriggerValueKey,
                    SetTriggerValue: scriptFunction.setTriggerValue,
                    Divorce: scriptFunction.divorce,
                    PresentExp: scriptFunction.presentExp,
                    CollectMeso: scriptFunction.collectMeso,
                    MaidMoodIncrease: scriptFunction.maidMoodUp,
                    MaidClosenessIncrease: scriptFunction.maidAffinityUp,
                    MaidPay: scriptFunction.maidPay
                );
                if (!scriptDict.TryGetValue(scriptId, out Dictionary<int, ScriptFunctionMetadata>? functionDict)) {
                    functionDict = new Dictionary<int, ScriptFunctionMetadata> {
                        { scriptFunction.functionID, metadata },
                    };
                    scriptDict.Add(scriptId, functionDict);
                } else {
                    functionDict.Add(scriptFunction.functionID, metadata);
                }
            }
            results.Add(questId, scriptDict);
        }
        return results;
    }

    private JobConditionTable ParseJobCondition() {
        var results = new Dictionary<int, JobConditionMetadata>();
        foreach ((int npcId, Parser.Xml.Table.Server.JobConditionTable jobCondition) in parser.ParseJobConditionTable()) {
            DateTime date = DateTime.TryParseExact(jobCondition.date, "yyyy-MM-dd-HH-mm-ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out date) ? date : DateTime.MinValue;
            results.Add(npcId, new JobConditionMetadata(
                NpcId: npcId,
                ScriptId: jobCondition.scriptID,
                StartedQuestId: jobCondition.quest_start,
                CompletedQuestId: jobCondition.quest_complete,
                JobCode: (JobCode) jobCondition.job,
                MaidAuthority: jobCondition.maid_auth,
                MaidClosenessTime: jobCondition.maid_affinity_time,
                MaidCosenessRank: jobCondition.maid_affinity_grade,
                Date: date.ToEpochSeconds(),
                BuffId: jobCondition.buff,
                Mesos: jobCondition.meso,
                Level: jobCondition.level,
                Home: jobCondition.home,
                Roulette: jobCondition.roulette,
                Guild: jobCondition.guild,
                CompletedAchievement: jobCondition.achieve_complete,
                IsBirthday: jobCondition.birthday,
                ChangeToJobCode: (JobCode) jobCondition.jobCode,
                MapId: jobCondition.map,
                MoveMapId: jobCondition.moveFieldID,
                MovePortalId: jobCondition.movePortalID,
                DeathPenalty: jobCondition.panelty
            ));
        }

        return new JobConditionTable(results);
    }

    private BonusGameTable ParseBonusGameTable() {
        var bonusGames = new Dictionary<int, BonusGameTable.Game>();
        foreach ((int type, int id, BonusGame bonusGame) in parser.ParseBonusGame()) {
            List<BonusGameTable.Game.Slot> slots = [];
            foreach (BonusGame.Slot slot in bonusGame.slot) {
                slots.Add(new BonusGameTable.Game.Slot(
                    MinProp: slot.minProp,
                    MaxProp: slot.maxProp));
            }
            bonusGames.Add(id, new BonusGameTable.Game(
                Id: id,
                Type: type,
                ConsumeItem: new ItemComponent(
                    ItemId: bonusGame.consumeItemID,
                    Rarity: 1,
                    Amount: bonusGame.consumeItemCount,
                    Tag: ItemTag.None),
                Slots: slots.ToArray()));
        }

        var drops = new Dictionary<int, BonusGameTable.Drop>();
        foreach ((int type, int id, BonusGameDrop gameDrop) in parser.ParseBonusGameDrop()) {
            List<BonusGameTable.Drop.Item> items = [];
            foreach (BonusGameDrop.Item item in gameDrop.item) {
                items.Add(new BonusGameTable.Drop.Item(
                    ItemComponent: new ItemComponent(
                        ItemId: item.id,
                        Rarity: item.rank,
                        Amount: item.count,
                        Tag: ItemTag.None),
                    Probability: item.prop,
                    Notice: item.notice));
            }
            drops.Add(id, new BonusGameTable.Drop(
                Id: id,
                Items: items.ToArray()));
        }

        return new BonusGameTable(bonusGames, drops);
    }

    private GlobalDropItemBoxTable ParseGlobalItemDropTable() {
        var dropGroups = new Dictionary<int, Dictionary<int, IList<GlobalDropItemBoxTable.Group>>>();

        foreach ((int id, GlobalDropItemBox itemDrop) in parser.ParseGlobalDropItemBox()) {
            var groups = new List<GlobalDropItemBoxTable.Group>();
            foreach (GlobalDropItemBox.Group group in itemDrop.v) {
                List<GlobalDropItemBoxTable.Group.DropCount> dropCounts = [];
                for (int i = 0; i < group.dropCount.Length; i++) {
                    dropCounts.Add(new GlobalDropItemBoxTable.Group.DropCount(
                        Amount: group.dropCount[i],
                        Probability: group.dropCountProbability[i]));
                }
                groups.Add(new GlobalDropItemBoxTable.Group(
                    GroupId: group.dropGroupIDs,
                    MinLevel: group.minLevel,
                    MaxLevel: group.maxLevel,
                    DropCounts: dropCounts,
                    OwnerDrop: group.isOwnerDrop,
                    MapTypeCondition: (MapType) group.mapTypeCondition,
                    ContinentCondition: (Continent) group.continentCondition));
            }

            if (!dropGroups.TryGetValue(id, out Dictionary<int, IList<GlobalDropItemBoxTable.Group>>? groupDict)) {
                groupDict = new Dictionary<int, IList<GlobalDropItemBoxTable.Group>> {
                    { id, groups },
                };
                dropGroups.Add(id, groupDict);
            } else {
                groupDict.Add(id, groups);
            }
        }

        var dropItems = new Dictionary<int, IList<GlobalDropItemBoxTable.Item>>();
        foreach ((int id, GlobalDropItemSet itemBox) in parser.ParseGlobalDropItemSet()) {
            var items = new List<GlobalDropItemBoxTable.Item>();

            foreach (GlobalDropItemSet.Item item in itemBox.v) {
                int minCount = item.minCount <= 0 ? 1 : item.minCount;
                int maxCount = item.maxCount < item.minCount ? item.minCount : item.maxCount;
                items.Add(new GlobalDropItemBoxTable.Item(
                    Id: item.itemID,
                    MinLevel: item.minLevel,
                    MaxLevel: item.maxLevel,
                    DropCount: new GlobalDropItemBoxTable.Range<int>(minCount, maxCount),
                    Rarity: item.grade,
                    Weight: item.weight,
                    MapIds: item.mapDependency,
                    QuestConstraint: item.constraintsQuest));
            }

            dropItems.Add(id, items);
        }
        return new GlobalDropItemBoxTable(dropGroups, dropItems);
    }

    private UserStatTable ParseUserStat() {
        static IReadOnlyDictionary<BasicAttribute, long> UserStatMetadataMapper(UserStat userStat) {
            Dictionary<BasicAttribute, long> stats = new() {
                { BasicAttribute.Strength, (long) userStat.str },
                { BasicAttribute.Dexterity, (long) userStat.dex },
                { BasicAttribute.Intelligence, (long) userStat.@int },
                { BasicAttribute.Luck, (long) userStat.luk },
                { BasicAttribute.Health, (long) userStat.hp },
                { BasicAttribute.HpRegen, (long) userStat.hp_rgp },
                { BasicAttribute.HpRegenInterval, (long) userStat.hp_inv },
                { BasicAttribute.Spirit, (long) userStat.sp },
                { BasicAttribute.SpRegen, (long) userStat.sp_rgp },
                { BasicAttribute.SpRegenInterval, (long) userStat.sp_inv },
                { BasicAttribute.Stamina, (long) userStat.ep },
                { BasicAttribute.StaminaRegen, (long) userStat.ep_rgp },
                { BasicAttribute.StaminaRegenInterval, (long) userStat.ep_inv },
                { BasicAttribute.AttackSpeed, (long) userStat.asp },
                { BasicAttribute.MovementSpeed, (long) userStat.msp },
                { BasicAttribute.Accuracy, (long) userStat.atp },
                { BasicAttribute.Evasion, (long) userStat.evp },
                { BasicAttribute.CriticalRate, (long) userStat.cap },
                { BasicAttribute.CriticalDamage, (long) userStat.cad },
                { BasicAttribute.CriticalEvasion, (long) userStat.car },
                { BasicAttribute.Defense, (long) userStat.ndd },
                { BasicAttribute.PerfectGuard, (long) userStat.abp },
                { BasicAttribute.JumpHeight, (long) userStat.jmp },
                { BasicAttribute.PhysicalAtk, (long) userStat.pap },
                { BasicAttribute.MagicalAtk, (long) userStat.map },
                { BasicAttribute.PhysicalRes, (long) userStat.par },
                { BasicAttribute.MagicalRes, (long) userStat.mar },
                { BasicAttribute.MinWeaponAtk, (long) userStat.wapmin },
                { BasicAttribute.MaxWeaponAtk, (long) userStat.wapmax },
                { BasicAttribute.Damage, (long) userStat.dmg },
                { BasicAttribute.Piercing, (long) userStat.pen },
                { BasicAttribute.BonusAtk, (long) userStat.base_atk },
                { BasicAttribute.PetBonusAtk, (long) userStat.sp_value },
            };

            return stats;
        }

        return new UserStatTable(
            new Dictionary<JobCode, IReadOnlyDictionary<short, IReadOnlyDictionary<BasicAttribute, long>>> {
                { JobCode.Newbie, parser.ParseUserStat1().ToDictionary(x => x.Level, x => UserStatMetadataMapper(x.UserStat)) },
                { JobCode.Knight, parser.ParseUserStat10().ToDictionary(x => x.Level, x => UserStatMetadataMapper(x.UserStat)) },
                { JobCode.Berserker, parser.ParseUserStat20().ToDictionary(x => x.Level, x => UserStatMetadataMapper(x.UserStat)) },
                { JobCode.Wizard, parser.ParseUserStat30().ToDictionary(x => x.Level, x => UserStatMetadataMapper(x.UserStat)) },
                { JobCode.Priest, parser.ParseUserStat40().ToDictionary(x => x.Level, x => UserStatMetadataMapper(x.UserStat)) },
                { JobCode.Archer, parser.ParseUserStat50().ToDictionary(x => x.Level, x => UserStatMetadataMapper(x.UserStat)) },
                { JobCode.HeavyGunner, parser.ParseUserStat60().ToDictionary(x => x.Level, x => UserStatMetadataMapper(x.UserStat)) },
                { JobCode.Thief, parser.ParseUserStat70().ToDictionary(x => x.Level, x => UserStatMetadataMapper(x.UserStat)) },
                { JobCode.Assassin, parser.ParseUserStat80().ToDictionary(x => x.Level, x => UserStatMetadataMapper(x.UserStat)) },
                { JobCode.RuneBlader, parser.ParseUserStat90().ToDictionary(x => x.Level, x => UserStatMetadataMapper(x.UserStat)) },
                { JobCode.Striker, parser.ParseUserStat100().ToDictionary(x => x.Level, x => UserStatMetadataMapper(x.UserStat)) },
                { JobCode.SoulBinder, parser.ParseUserStat110().ToDictionary(x => x.Level, x => UserStatMetadataMapper(x.UserStat)) },
                { JobCode.GameMaster, parser.ParseUserStat999().ToDictionary(x => x.Level, x => UserStatMetadataMapper(x.UserStat)) },
            }
        );
    }

    // The item ids of the played client's item data (PlayedClientItems), null without a played client or when
    // its pack has no item data.
    private HashSet<int>? playedItemIds;
    private HashSet<int>? PlayedItemIds() {
        if (playedReader == null) {
            return null;
        }
        if (playedItemIds == null) {
            playedItemIds = PlayedClientItems.Ids(
                playedReader.Files.Where(file => PlayedClientItems.IsItemFile(file.Name)).Select(file => playedReader.GetXmlDocument(file)),
                node => FeatureLocaleFilter.FeatureEnabled(node.GetAttribute("feature")) && FeatureLocaleFilter.HasLocale(node.GetAttribute("locale")));
            Console.WriteLine($"played client: {playedItemIds.Count} items in its item data");
        }
        return playedItemIds.Count == 0 ? null : playedItemIds;
    }

    private IndividualDropItemTable ParseIndividualItemDropTable() {
        var results = new Dictionary<int, IDictionary<int, IndividualDropItemTable.Entry>>();

        XmlDocument? clientTable = ClientListedRows.PlayedTable(playedReader, FeatureLocaleFilter.Locale, "individualitemdrop_final.xml");
        IEnumerable<(int Id, IndividualItemDrop Box)> dropBoxes = ServerDropTable.Read(xmlReader, dropFile, clientTable, out ClientDropBoxes.Counts? clientCounts);
        foreach ((int id, IndividualItemDrop dropBox) in dropBoxes) {
            var entries = new Dictionary<int, IndividualDropItemTable.Entry>();

            foreach (IndividualItemDrop.Group group in dropBox.group) {
                List<IndividualDropItemTable.Item> items = [];
                foreach (IndividualItemDrop.Group.Item item in group.v) {
                    int minCount = item.minCount <= 0 ? 1 : item.minCount;
                    int maxCount = item.maxCount < item.minCount ? item.minCount : item.maxCount;
                    List<IndividualDropItemTable.Item.Rarity> rarities = item.gradeProbability
                        .Select((probability, i) => new IndividualDropItemTable.Item.Rarity(probability, item.grade[i]))
                        .ToList();

                    if (rarities.Count == 0) {
                        if (item.grade.Length > 0) {
                            foreach (short grade in item.grade) {
                                rarities.Add(new IndividualDropItemTable.Item.Rarity(100, grade));
                            }
                        } else if (item.uiItemRank != 0) {
                            rarities.Add(new IndividualDropItemTable.Item.Rarity(100, item.uiItemRank));
                        }
                    }
                    items.Add(new IndividualDropItemTable.Item(
                        Ids: [item.itemID, item.itemID2],
                        Announce: item.isAnnounce,
                        ProperJobWeight: item.properJobWeight,
                        ImproperJobWeight: item.imProperJobWeight,
                        Weight: item.weight,
                        DropCount: new IndividualDropItemTable.Range<int>(minCount, maxCount),
                        Rarities: rarities,
                        EnchantLevel: item.enchantLevel,
                        SocketDataId: item.socketDataID,
                        DeductTradeCount: item.tradableCountDeduction,
                        DeductRepackLimit: item.rePackingLimitCountDeduction,
                        Bind: item.isBindCharacter,
                        DisableBreak: item.disableBreak,
                        MapIds: item.mapDependency,
                        // constraintsQuest: the row drops only for a player whose started quest still needs the item
                        // (ItemDropManager); the quest is found by its item_exist condition, not named in the row.
                        QuestConstraint: item.constraintsQuest
                    ));
                }

                IList<IndividualDropItemTable.Entry.DropCount> dropCounts = group.dropCount.Zip(group.dropCountProbability, (count, probability) => new IndividualDropItemTable.Entry.DropCount(count, probability)).ToList();
                if (dropCounts.Count == 0) {
                    dropCounts.Add(new IndividualDropItemTable.Entry.DropCount(1, 100));
                }

                var entry = new IndividualDropItemTable.Entry(
                    GroupId: group.dropGroupID,
                    SmartDropRate: group.smartDropRate,
                    DropCounts: dropCounts,
                    MinLevel: group.dropGroupMinLevel,
                    ServerDrop: group.serverDrop,
                    SmartGender: group.isApplySmartGenderDrop,
                    Items: items
                );

                entries.Add(group.dropGroupID, entry);

            }

            results.Add(id, entries);
        }
        if (clientCounts != null) {
            Console.WriteLine($"individualItemDrop: {clientCounts.Replaced + clientCounts.Appended} boxes without an available row taken from the played client's table ({clientCounts.Replaced} replaced, {clientCounts.Appended} added); {clientCounts.ServerGroups} groups with the server's weights, {clientCounts.ClientGroups} with the client's rows only");
        }
        Console.WriteLine($"individualItemDrop: {dropFile} read, {results.Count} boxes, {results.Values.Sum(box => box.Values.Sum(group => group.Items.Count))} rows after the locale and feature filter");

        // A played client of another version has no item data for some of the server's items; their rows are
        // left out (PlayedClientItems), and a box left with no rows goes.
        HashSet<int>? known = PlayedItemIds();
        if (known == null) {
            return new IndividualDropItemTable(results);
        }
        Dictionary<int, IDictionary<int, IndividualDropItemTable.Entry>> kept = PlayedClientItems.Keep(results, known, out PlayedClientItems.Count count, out int boxesDropped);
        Console.WriteLine($"individualItemDrop: {count} (items not in the played client's item data), {boxesDropped} boxes left empty dropped");
        return new IndividualDropItemTable(kept);
    }

    private PrestigeExpTable ParsePrestigeExpTable() {
        var results = new Dictionary<ExpType, long>();

        foreach ((AdventureExpType type, AdventureExpTable table) in parser.ParseAdventureExp()) {
            ExpType expType = ToExpType(type);
            results.Add(expType, table.value);
        }

        return new PrestigeExpTable(results);
    }

    private PrestigeIdExpTable ParsePrestigeIdExpTable() {
        var results = new Dictionary<int, PrestigeIdExpTable.Entry>();
        foreach ((int id, AdventureIdExpTable table) in parser.ParseAdventureIdExp()) {
            results.Add(id, new PrestigeIdExpTable.Entry(
                Id: id,
                Value: table.value,
                Type: ToExpType(table.expType)));
        }

        return new PrestigeIdExpTable(results);
    }

    private static ExpType ToExpType(AdventureExpType type) {
        return type switch {
            AdventureExpType.Exp_MapCommon => ExpType.mapCommon,
            AdventureExpType.Exp_MapHidden => ExpType.mapHidden,
            AdventureExpType.Exp_TaxiStation => ExpType.taxi,
            AdventureExpType.Exp_Telescope => ExpType.telescope,
            AdventureExpType.Exp_RareChest => ExpType.rareChest,
            AdventureExpType.Exp_RareChestFirst => ExpType.rareChestFirst,
            AdventureExpType.Exp_NormalChest => ExpType.normalChest,
            AdventureExpType.Exp_DropItem => ExpType.dropItem,
            AdventureExpType.Exp_DungeonBoss => ExpType.dungeonBoss,
            AdventureExpType.Exp_MusicMasteryLv1 => ExpType.musicMastery1,
            AdventureExpType.Exp_MusicMasteryLv2 => ExpType.musicMastery2,
            AdventureExpType.Exp_MusicMasteryLv3 => ExpType.musicMastery3,
            AdventureExpType.Exp_MusicMasteryLv4 => ExpType.musicMastery4,
            AdventureExpType.Exp_Arcade => ExpType.arcade,
            AdventureExpType.Exp_Fishing => ExpType.fishing,
            AdventureExpType.Exp_Rest => ExpType.rest,
            AdventureExpType.Exp_Quest => ExpType.quest,
            AdventureExpType.Exp_PvpBloodMineRank1 => ExpType.bloodMineRank1,
            AdventureExpType.Exp_PvpBloodMineRank2 => ExpType.bloodMineRank2,
            AdventureExpType.Exp_PvpBloodMineRank3 => ExpType.bloodMineRank3,
            AdventureExpType.Exp_PvpBloodMineRankOther => ExpType.bloodMineRankOther,
            AdventureExpType.Exp_PvpRedDuelWin => ExpType.redDuelWin,
            AdventureExpType.Exp_PvpRedDuelLose => ExpType.redDuelLose,
            AdventureExpType.Exp_PvpBtiTeamWin => ExpType.btiTeamWin,
            AdventureExpType.Exp_PvpBtiTeamLose => ExpType.btiTeamLose,
            AdventureExpType.Exp_PvpRankDuelWin => ExpType.rankDuelWin,
            AdventureExpType.Exp_PvpRankDuelLose => ExpType.rankDuelLose,
            AdventureExpType.Exp_Gathering => ExpType.gathering,
            AdventureExpType.Exp_Manufacturing => ExpType.manufacturing,
            AdventureExpType.Exp_RandomDungeonBonus => ExpType.randomDungeonBonus,
            AdventureExpType.Exp_MiniGame => ExpType.miniGame,
            AdventureExpType.Exp_UserMiniGame => ExpType.userMiniGame,
            AdventureExpType.Exp_UserMiniGameExtra => ExpType.userMiniGameExtra,
            AdventureExpType.Exp_Mission => ExpType.mission,
            AdventureExpType.Exp_DungeonRelative => ExpType.dungeonRelative,
            AdventureExpType.Exp_GuildUserExp => ExpType.guildUserExp,
            AdventureExpType.Exp_DailyGuildQuest => ExpType.dailyGuildQuest,
            AdventureExpType.Exp_WeeklyGuildQuest => ExpType.weeklyGuildQuest,
            AdventureExpType.Exp_PetTaming => ExpType.petTaming,
            AdventureExpType.Exp_DailyMission => ExpType.dailymission,
            AdventureExpType.Exp_DailyMissionLevelUp => ExpType.dailymissionLevelUp,
            AdventureExpType.Exp_mapleSurvival => ExpType.mapleSurvival,
            AdventureExpType.Exp_DarkStream => ExpType.darkStream,
            AdventureExpType.Exp_DungeonClear => ExpType.dungeonClear,
            AdventureExpType.Exp_KillMonster => ExpType.monster,
            AdventureExpType.Exp_QuestETC => ExpType.questEtc,
            AdventureExpType.Exp_EpicQuest => ExpType.epicQuest,
            AdventureExpType.Exp_KillMonsterBoss => ExpType.monsterBoss,
            AdventureExpType.Exp_KillMonsterElite => ExpType.monsterElite,
            _ => ExpType.none,
        };
    }

    private TimeEventTable ParseTimeEventTable() {
        var globalPortals = new Dictionary<int, GlobalPortalMetadata>();
        var worldBosses = new Dictionary<int, WorldBossMetadata>();
        var fieldWarEvents = new Dictionary<int, FieldWarEventMetadata>();
        var fieldInteractEvents = new Dictionary<int, FieldInteractEventMetadata>();
        var fieldTriggerEvents = new Dictionary<int, FieldTriggerEventMetadata>();
        int fieldInteractDuplicates = 0;
        int fieldInteractSkipped = 0;
        int fieldTriggerSkipped = 0;
        foreach ((int id, TimeEventData data) in parser.ParseTimeEventData()) {
            // Type 7: the field war (rows 85000 and 85001, every 2 hours, open 5 minutes). The parser keeps no
            // feature for these rows; the wars of fieldwardata are filtered by theirs, so a locale without them opens none.
            if ((int) data.type == FieldWarRules.TimeEventType) {
                int[] startTimeArray = ParseTimeToArray(data.startTime);
                int[] endTimeArray = ParseTimeToArray(data.endTime);
                int[] cycleArray = string.IsNullOrEmpty(data.cycleTime) ? [0, 0, 0, 0, 0, 0] : ParseTimeToArray(data.cycleTime);
                int[] randomArray = string.IsNullOrEmpty(data.randomTime) ? [0, 0, 0, 0, 0, 0] : ParseTimeToArray(data.randomTime);
                int[] lifeArray = string.IsNullOrEmpty(data.lifeTime) ? [0, 0, 0, 0, 0, 0] : ParseTimeToArray(data.lifeTime);
                fieldWarEvents.Add(id, new FieldWarEventMetadata(
                    Id: id,
                    Probability: data.prob,
                    StartTime: new DateTime(startTimeArray[0], startTimeArray[1], startTimeArray[2], startTimeArray[3], startTimeArray[4], startTimeArray[5]),
                    EndTime: new DateTime(endTimeArray[0], endTimeArray[1], endTimeArray[2], endTimeArray[3], endTimeArray[4], endTimeArray[5]),
                    CycleTime: new TimeSpan(cycleArray[2], cycleArray[3], cycleArray[4], cycleArray[5]),
                    RandomTime: new TimeSpan(randomArray[2], randomArray[3], randomArray[4], randomArray[5]),
                    LifeTime: new TimeSpan(lifeArray[2], lifeArray[3], lifeArray[4], lifeArray[5]),
                    GroupId: data.fieldWarGroupID));
                continue;
            }
            // Type 4: the field interact objects. Rows with one id are locale variants; the client keeps the rows
            // IsElementAvailable allows (tableexporter.cpp), the same here.
            if (data.type == TimeEventType.FieldInteractObject) {
                if (!FeatureLocaleFilter.FeatureEnabled(data.Feature) || !FeatureLocaleFilter.HasLocale(data.Locale) || data.interactID == 0) {
                    continue;
                }
                // A row whose times do not read (another locale's data, a later client) is skipped, not the ingest.
                if (!TryParseDate(data.startTime, out DateTime startTime) || !TryParseDate(data.endTime, out DateTime endTime)
                    || !TryParseSpan(data.cycleTime, out TimeSpan cycleTime) || !TryParseSpan(data.randomTime, out TimeSpan randomTime)
                    || !TryParseSpan(data.lifeTime, out TimeSpan lifeTime)) {
                    fieldInteractSkipped++;
                    continue;
                }
                if (fieldInteractEvents.ContainsKey(id)) {
                    fieldInteractDuplicates++;
                }
                fieldInteractEvents[id] = new FieldInteractEventMetadata(
                    Id: id,
                    Probability: data.prob,
                    StartTime: startTime,
                    EndTime: endTime,
                    CycleTime: cycleTime,
                    RandomTime: randomTime,
                    LifeTime: lifeTime,
                    MapIds: data.targetFields,
                    SpawnPointIds: data.targetSpawnPointIDs,
                    SpawnTags: data.targetSpawnTags.Select(tag => tag.Trim()).Where(tag => tag.Length > 0).ToArray(),
                    InteractCount: data.interactCount,
                    VariableCountByChannel: data.variableCountByChannelCount,
                    InteractId: data.interactID,
                    Model: data.interactModel,
                    Asset: data.interactAsset,
                    Normal: data.interactNormal,
                    Reactable: data.interactReactable,
                    DurationStartHour: data.eventDurationStartHour,
                    DurationStartMin: data.eventDurationStartMin,
                    DurationEndHour: data.eventDurationEndHour,
                    DurationEndMin: data.eventDurationEndMin);
                continue;
            }
            // Type 6: a user value the row gives a trigger of its fields at every run (EventStart or MelodyOn of the
            // TimeEvent_triggerValue scripts of the Kritias fields). The client does not read these rows.
            if (data.type == TimeEventType.KritiasChestPuzzle) {
                if (!FeatureLocaleFilter.FeatureEnabled(data.Feature) || !FeatureLocaleFilter.HasLocale(data.Locale)
                    || data.triggerID == 0 || string.IsNullOrEmpty(data.key)) {
                    continue;
                }
                if (!TryParseDate(data.startTime, out DateTime startTime) || !TryParseDate(data.endTime, out DateTime endTime)
                    || !TryParseSpan(data.cycleTime, out TimeSpan cycleTime) || !TryParseSpan(data.randomTime, out TimeSpan randomTime)
                    || !TryParseSpan(data.lifeTime, out TimeSpan lifeTime)) {
                    fieldTriggerSkipped++;
                    continue;
                }
                fieldTriggerEvents[id] = new FieldTriggerEventMetadata(
                    Id: id,
                    Probability: data.prob,
                    StartTime: startTime,
                    EndTime: endTime,
                    CycleTime: cycleTime,
                    RandomTime: randomTime,
                    LifeTime: lifeTime,
                    MapIds: data.targetFields,
                    TriggerId: data.triggerID,
                    Key: data.key,
                    Value: data.value);
                continue;
            }
            switch (data.type) {
                case TimeEventType.GlobalEvent: {
                        var entries = new GlobalPortalMetadata.Field[3]; // UI only supports 3 fields
                        entries[0] = new GlobalPortalMetadata.Field(
                            Name: data.eventName1,
                            MapId: data.eventField1.Length == 0 ? 0 : data.eventField1[0],
                            PortalId: data.eventField1.Length == 0 ? 0 : data.eventField1[1]);
                        entries[1] = new GlobalPortalMetadata.Field(
                            Name: data.eventName2,
                            MapId: data.eventField2.Length == 0 ? 0 : data.eventField2[0],
                            PortalId: data.eventField2.Length == 0 ? 0 : data.eventField2[1]);
                        entries[2] = new GlobalPortalMetadata.Field(
                            Name: data.eventName3,
                            MapId: data.eventField3.Length == 0 ? 0 : data.eventField3[0],
                            PortalId: data.eventField3.Length == 0 ? 0 : data.eventField3[1]);
                        int[] startTimeArray = ParseTimeToArray(data.startTime);
                        int[] endTimeArray = ParseTimeToArray(data.endTime);
                        int[] cycleArray = ParseTimeToArray(data.cycleTime);
                        int[] randomArray = ParseTimeToArray(data.randomTime);
                        int[] lifeArray = ParseTimeToArray(data.lifeTime);
                        globalPortals.Add(id, new GlobalPortalMetadata(
                            Id: id,
                            Probability: data.prob,
                            StartTime: new DateTime(startTimeArray[0], startTimeArray[1], startTimeArray[2], startTimeArray[3], startTimeArray[4], startTimeArray[5]),
                            EndTime: new DateTime(endTimeArray[0], endTimeArray[1], endTimeArray[2], endTimeArray[3], endTimeArray[4], endTimeArray[5]),
                            CycleTime: new TimeSpan(cycleArray[2], cycleArray[3], cycleArray[4], cycleArray[5]),
                            RandomTime: new TimeSpan(randomArray[2], randomArray[3], randomArray[4], randomArray[5]),
                            LifeTime: new TimeSpan(lifeArray[2], lifeArray[3], lifeArray[4], lifeArray[5]),
                            PopupMessage: data.popupMessage,
                            SoundId: data.soundID,
                            Entries: entries));
                        break;
                    }
                case TimeEventType.Boss: {
                        int[] startTimeArray = ParseTimeToArray(data.startTime);
                        int[] endTimeArray = ParseTimeToArray(data.endTime);
                        int[] cycleArray = string.IsNullOrEmpty(data.cycleTime) ? [0, 0, 0, 0, 0, 0] : ParseTimeToArray(data.cycleTime);
                        int[] randomArray = string.IsNullOrEmpty(data.randomTime) ? [0, 0, 0, 0, 0, 0] : ParseTimeToArray(data.randomTime);
                        int[] lifeArray = string.IsNullOrEmpty(data.lifeTime) ? [0, 0, 0, 0, 0, 0] : ParseTimeToArray(data.lifeTime);
                        worldBosses.Add(id, new WorldBossMetadata(
                            Id: id,
                            Probability: data.prob,
                            StartTime: new DateTime(startTimeArray[0], startTimeArray[1], startTimeArray[2], startTimeArray[3], startTimeArray[4], startTimeArray[5]),
                            EndTime: new DateTime(endTimeArray[0], endTimeArray[1], endTimeArray[2], endTimeArray[3], endTimeArray[4], endTimeArray[5]),
                            CycleTime: new TimeSpan(cycleArray[2], cycleArray[3], cycleArray[4], cycleArray[5]),
                            RandomTime: new TimeSpan(randomArray[2], randomArray[3], randomArray[4], randomArray[5]),
                            LifeTime: new TimeSpan(lifeArray[2], lifeArray[3], lifeArray[4], lifeArray[5]),
                            TargetMapIds: data.targetFields,
                            SpawnPointIds: data.targetSpawnPointIDs,
                            NpcIds: data.npcIDs,
                            Tag: data.tag,
                            Unique: data.unique,
                            IndividualChannelSpawn: data.individualChannelSpawn,
                            VariableCountByChannel: data.variableCountByChannelCount,
                            ScreenNotice: data.screenNotice,
                            ChatNotice: data.chatNotice));
                        break;
                    }
            }
        }
        Dictionary<int, FieldWarMetadata> fieldWars = ParseFieldWarData();
        Console.WriteLine($"timeEventData: {fieldWarEvents.Count} field war events, {fieldWars.Count} field wars, "
                          + $"{fieldInteractEvents.Count} field interact events ({fieldInteractDuplicates} ids kept twice, the last row wins; "
                          + $"{fieldInteractSkipped} rows skipped, times unreadable), "
                          + $"{fieldTriggerEvents.Count} field trigger events ({fieldTriggerSkipped} rows skipped, times unreadable)");
        return new TimeEventTable(GlobalPortal: globalPortals, WorldBoss: worldBosses, FieldWarEvent: fieldWarEvents, FieldWar: fieldWars,
            FieldInteractEvent: fieldInteractEvents, FieldTriggerEvent: fieldTriggerEvents);

        // A type 4 time: year-month-day-hour-minute-second, every part a number and the date a real one.
        bool TryParseDate(string? time, out DateTime value) {
            value = default;
            if (!TryParseParts(time, out int[] parts)) {
                return false;
            }
            try {
                value = new DateTime(parts[0], parts[1], parts[2], parts[3], parts[4], parts[5]);
                return true;
            } catch (ArgumentOutOfRangeException) {
                return false;
            }
        }

        // A type 4 span in the same form (the day, hour, minute and second parts); empty: none.
        bool TryParseSpan(string? time, out TimeSpan value) {
            value = TimeSpan.Zero;
            if (string.IsNullOrEmpty(time)) {
                return true;
            }
            if (!TryParseParts(time, out int[] parts)) {
                return false;
            }
            try {
                value = new TimeSpan(parts[2], parts[3], parts[4], parts[5]);
                return true;
            } catch (ArgumentOutOfRangeException) {
                return false;
            }
        }

        bool TryParseParts(string? time, out int[] parts) {
            parts = [];
            if (string.IsNullOrWhiteSpace(time)) {
                return false;
            }
            string[] texts = time.Trim().Split(['-', ':']);
            if (texts.Length != 6) {
                return false;
            }
            parts = new int[6];
            for (int i = 0; i < 6; i++) {
                if (!int.TryParse(texts[i], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out parts[i])) {
                    return false;
                }
            }
            return true;
        }

        // year-month-day-hour-minute-second; 201 type 4 rows write the time of day with colons (2099-12-31-00:00:00).
        int[] ParseTimeToArray(string time) {
            string[] timeArray = time.Split(['-', ':']);
            int[] timeInt = new int[timeArray.Length];
            for (int i = 0; i < timeArray.Length; i++) {
                timeInt[i] = int.Parse(timeArray[i]);
            }
            return timeInt;
        }
    }

    // The client's table/fieldwardata.xml (the parser has no reader for it): fieldWarID, fieldWarGroupID, pickWeight,
    // fieldID, rewardID, uiFieldID, feature and locale. The client announces only a war it has in this table.
    // FieldWarDataTable::Load (commonfiles/fieldwardata.cpp) reads the rows FUN_14028faa0 (commonutil.cpp) keeps: the
    // available ones (feature and locale), and of rows with one fieldWarID the one SelectFeatureElement picks.
    private Dictionary<int, FieldWarMetadata> ParseFieldWarData() {
        Maple2.File.IO.Crypto.Common.PackFileEntry? entry = clientReader?.Files.FirstOrDefault(file =>
            file.Name.Equals("table/fieldwardata.xml", StringComparison.OrdinalIgnoreCase));
        return entry == null ? new Dictionary<int, FieldWarMetadata>()
            : MapFieldWarData(clientReader!.GetXmlDocument(entry),
                row => FeatureLocaleFilter.FeatureEnabled(row.GetAttribute("feature")) && FeatureLocaleFilter.HasLocale(row.GetAttribute("locale")),
                (a, b) => PlayedNpcStats.SelectFeatureElement(a, b, feature => FeatureLocaleFilter.Features.GetValueOrDefault(feature), FeatureLocaleFilter.Locale));
    }

    public static Dictionary<int, FieldWarMetadata> MapFieldWarData(XmlDocument document, Func<XmlElement, bool> available,
            Func<XmlElement, XmlElement, XmlElement> select) {
        var results = new Dictionary<int, FieldWarMetadata>();
        if (document.DocumentElement == null) {
            return results;
        }
        // Keyed by the fieldWarID text, as the client's.
        var rows = new Dictionary<string, XmlElement>();
        foreach (XmlNode node in document.DocumentElement.ChildNodes) {
            if (node is not XmlElement { Name: "fieldWar" } row || !available(row)) {
                continue;
            }
            string key = row.GetAttribute("fieldWarID");
            rows[key] = rows.TryGetValue(key, out XmlElement? kept) ? select(kept, row) : row;
        }
        foreach (XmlElement row in rows.Values) {
            if (!int.TryParse(row.GetAttribute("fieldWarID"), out int id) || !int.TryParse(row.GetAttribute("fieldID"), out int mapId)) {
                continue;
            }
            int.TryParse(row.GetAttribute("fieldWarGroupID"), out int groupId);
            if (!int.TryParse(row.GetAttribute("pickWeight"), out int pickWeight)) {
                pickWeight = 1;
            }
            int.TryParse(row.GetAttribute("rewardID"), out int rewardId);
            int.TryParse(row.GetAttribute("uiFieldID"), out int uiMapId);
            results[id] = new FieldWarMetadata(id, groupId, pickWeight, mapId, rewardId, uiMapId);
        }
        return results;
    }

    private GameEventTable ParseGameEventTable() {
        var results = new Dictionary<int, GameEventMetadata>();
        DateTime now = AttendGiftReplay.IngestNow();
        var ended = new List<GameEventMetadata>();
        foreach ((int id, GameEvent data) in overlay.GameEvents(parser.ParseGameEvent())) {
            if (!Enum.TryParse(data.eventType, out GameEventType eventType)) {
                Console.WriteLine($"Unknown GameEventType: {data.eventType}");
            }

            GameEventData? eventData = ParseGameEventData(eventType, data.value1, data.value2, data.value3, data.value4);
            if (eventData == null) {
                continue;
            }

            if (eventData is Snowman snowman) {
                eventData = snowman with { Sources = SnowmanSources(data.eventStart, data.eventEnd) };
            }
            GameEventMetadata metadata = MapGameEvent(id, data, eventType, eventData);
            DateTime endTime = metadata.EndTime;

            // An NpcShopShowItem row without a readable end (DateTime.MinValue) is dropped: kept as ended it would hide
            // the items it names for good.
            if (eventType == GameEventType.NpcShopShowItem && endTime == DateTime.MinValue) {
                Console.WriteLine($"NpcShopShowItem {id}: no readable eventEnd \"{data.eventEnd}\", row dropped");
                continue;
            }

            // Only add events that are not expired. An ended NpcShopShowItem row stays: the items it names are hidden
            // while none of their rows runs (an ended row is never active, so nothing else reads it).
            if (endTime < now && eventType != GameEventType.NpcShopShowItem) {
                ended.Add(metadata);
                continue;
            }
            results.Add(id, metadata);
        }

        if (replayAttendGift) {
            // The rank the client's item data gives an item id (0: not there, or no rank); the played client's
            // item data when MS2_PLAYED_CLIENT_XML names it.
            int ClientRank(int itemId) =>
                ItemMapper.ItemMetadataById.TryGetValue(itemId, out ItemMetadata? item) && !item.Property.NotInPlayedClient
                    ? item.Property.Rank ?? 0 : 0;
            // The quests of the played client's quest table (PlayedClientQuests); without a played client every
            // quest of the data counts as known.
            HashSet<int>? playedQuests = null;
            Maple2.File.IO.Crypto.Common.PackFileEntry? questTable = playedReader?.Files.FirstOrDefault(file =>
                string.Equals(file.Name, PlayedClientQuests.QuestTable, StringComparison.OrdinalIgnoreCase));
            if (playedReader != null && questTable != null) {
                playedQuests = PlayedClientQuests.Ids(playedReader.GetXmlDocument(questTable), node => FeatureLocaleFilter.FeatureEnabled(node.GetAttribute("feature")));
                if (playedQuests.Count == 0) {
                    playedQuests = null;
                }
            }
            bool ClientQuest(int questId) => playedQuests == null || playedQuests.Contains(questId);
            // An event whose data was not read (null) is not a candidate.
            GameEventMetadata[] readableEvents = ended.Where(e => !AttendGiftReplay.NeedData.Contains(e.Type) || e.Data != null).ToArray();
            // An event with a play item is taken only with a row that gives it (AttendGiftReplay.Companions), whose
            // items the client has too.
            bool Playable(GameEventMetadata e) => AttendGiftReplay.PlayItem(e.Data) == 0
                || AttendGiftReplay.Companions(e, ended).Any(companion => AttendGiftReplay.ClientHasRewards(companion, ClientRank));
            foreach (GameEventType type in AttendGiftReplay.Types)
                foreach (GameEventMetadata[] readable in AttendGiftReplay.Pools(readableEvents, type)) {
                    GameEventMetadata? replay = AttendGiftReplay.Pick(readable, now, type, ClientRank, ClientQuest, Playable);
                    int[] passed = AttendGiftReplay.Candidates(readable, now, type).TakeWhile(e => e != replay).Select(e => e.Id).ToArray();
                    if (passed.Length > 0) {
                        Console.WriteLine($"{AttendGiftReplay.Option}: {type} {string.Join(", ", passed)} not replayed, a reward item has no rank in the client's item data"
                                          + (type is GameEventType.StampEvent or GameEventType.TimeRunEvent ? " or a quest is not in the client's quest table" : string.Empty)
                                          + (type is GameEventType.RPS or GameEventType.BingoEvent or GameEventType.TimeRunEvent ? " or no ended row gives its play item" : string.Empty));
                    }
                    if (replay == null) {
                        Console.WriteLine($"{AttendGiftReplay.Option}: no ended {type} event to replay");
                    } else if (!results.ContainsKey(replay.Id)) {
                        GameEventMetadata moved = AttendGiftReplay.Redate(replay, now); // the local ingest day
                        results.Add(moved.Id, moved);
                        Console.WriteLine($"{AttendGiftReplay.Option}: {type} {replay.Id} ({replay.StartTime:yyyy-MM-dd HH:mm} to {replay.EndTime:yyyy-MM-dd HH:mm}) replayed {moved.StartTime:yyyy-MM-dd HH:mm} to {moved.EndTime:yyyy-MM-dd HH:mm}");
                        foreach (GameEventMetadata companion in AttendGiftReplay.Companions(replay, ended)) {
                            if (results.ContainsKey(companion.Id) || !AttendGiftReplay.ClientHasRewards(companion, ClientRank)) {
                                continue;
                            }
                            GameEventMetadata movedCompanion = AttendGiftReplay.Shift(companion, moved.StartTime - replay.StartTime);
                            results.Add(movedCompanion.Id, movedCompanion);
                            Console.WriteLine($"{AttendGiftReplay.Option}: {companion.Type} {companion.Id} replayed with {replay.Id}, {movedCompanion.StartTime:yyyy-MM-dd HH:mm} to {movedCompanion.EndTime:yyyy-MM-dd HH:mm}");
                        }
                    }
                }
        }
        return new GameEventTable(results);
    }

    // A DailyLoginReward row: value1 the mail content id (empty on a few rows: a mail without a text), value2 the
    // items, "id,count,rank,0" separated by '|'. An item without a readable id, count or rank, or of count 0, is
    // left out; a row without an item mails nothing and is not read. value3's first field is the lowest level
    // that gets the mail (chosen here: no client code reads it, and its values, 0, 1, 10, 50 and 60, are levels);
    // its second field ("10,1") and value4 ("1") are not read: nothing says what they are.
    public static DailyLoginReward? ReadDailyLoginReward(string value1, string value2, string value3 = "") {
        var items = new List<RewardItem>();
        foreach (string text in value2.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
            string[] fields = text.Split(',', StringSplitOptions.TrimEntries);
            if (fields.Length < 3 || !int.TryParse(fields[0], out int itemId) || itemId <= 0
                || !int.TryParse(fields[1], out int count) || count <= 0 || !short.TryParse(fields[2], out short rank)) {
                continue;
            }
            items.Add(new RewardItem(itemId: itemId, amount: count, rarity: rank));
        }
        if (items.Count == 0) {
            return null;
        }
        int minLevel = int.TryParse(value3.Split(',', 2)[0].Trim(), out int level) && level > 0 ? level : 0;
        return new DailyLoginReward(MailId: int.TryParse(value1, out int mailId) ? mailId : 0, Items: items.ToArray(), MinLevel: minLevel);
    }

    // The time run's rewards in value2's <ms>: a <reward> row per step at its distance, and the <final> row. A row
    // without a readable item id, count or grade is skipped; the first row of a distance counts.
    public static (Dictionary<int, RewardItem> Steps, RewardItem? Final) ReadTimeRunRewards(XmlNode ms) {
        var steps = new Dictionary<int, RewardItem>();
        RewardItem? final = null;
        foreach (XmlNode node in ms.ChildNodes) {
            if (node.Name is not ("reward" or "final")
                || !int.TryParse(node.Attributes?["itemID"]?.Value, out int itemId)
                || !int.TryParse(node.Attributes?["count"]?.Value, out int count)
                || !short.TryParse(node.Attributes?["grade"]?.Value, out short grade)) {
                continue;
            }
            var item = new RewardItem(itemId: itemId, amount: count, rarity: grade);
            if (node.Name == "final") {
                final ??= item;
            } else if (int.TryParse(node.Attributes?["distance"]?.Value, out int distance)) {
                steps.TryAdd(distance, item);
            }
        }
        return (steps, final);
    }

    // A gameEvent row of the server's table: its times (written to the minute or to the second, 634 KR rows),
    // part times and days, with the event's data as read.
    public static GameEventMetadata MapGameEvent(int id, GameEvent data, GameEventType eventType, GameEventData eventData) {
        (TimeSpan partTimeStart, TimeSpan partTimeEnd) = ParsePartTime(data.partTime);
        return new GameEventMetadata(
            Id: id,
            Type: eventType,
            StartTime: AttendGiftReplay.ParseTime(data.eventStart),
            EndTime: AttendGiftReplay.ParseTime(data.eventEnd),
            StartPartTime: partTimeStart,
            EndPartTime: partTimeEnd,
            ActiveDays: data.dayOfWeek.Length == 0 ? [] : data.dayOfWeek.Select(ParseDayOfWeek).ToArray(),
            Data: eventData,
            Value1: data.value1,
            Value2: data.value2,
            Value3: data.value3,
            Value4: data.value4);
    }

    private static DayOfWeek ParseDayOfWeek(Maple2.File.Parser.Enum.DayOfWeek dayofWeek) {
        return dayofWeek switch {
            Maple2.File.Parser.Enum.DayOfWeek.sun => DayOfWeek.Sunday,
            Maple2.File.Parser.Enum.DayOfWeek.mon => DayOfWeek.Monday,
            Maple2.File.Parser.Enum.DayOfWeek.tue => DayOfWeek.Tuesday,
            Maple2.File.Parser.Enum.DayOfWeek.wed => DayOfWeek.Wednesday,
            Maple2.File.Parser.Enum.DayOfWeek.thu => DayOfWeek.Thursday,
            Maple2.File.Parser.Enum.DayOfWeek.fri => DayOfWeek.Friday,
            Maple2.File.Parser.Enum.DayOfWeek.sat => DayOfWeek.Saturday,
            _ => DayOfWeek.Sunday,
        };
    }

    private static (TimeSpan, TimeSpan) ParsePartTime(string partTimeString) {
        string[] partTimeStringArray = partTimeString.Split("-");
        if (partTimeStringArray.Length != 2) {
            return (TimeSpan.Zero, TimeSpan.Zero);
        }
        TimeSpan startTime = TimeSpan.Parse(partTimeStringArray[0]);
        TimeSpan endTime = TimeSpan.Parse(partTimeStringArray[1]);
        return (startTime, endTime);
    }

    // The special attributes whose value is a rate: valueType 2 in the client's
    // table/specialabilitystringtable.xml (LoadSpecialAbilityStringTable, commonfiles/specialability.cpp:170).
    // The played client's pack first, else the data folder's; empty without either.
    private HashSet<int>? specialRates;
    private HashSet<int> ClientSpecialRates() {
        var rates = new HashSet<int>();
        foreach (M2dReader? reader in new[] { playedReader, clientReader }) {
            Maple2.File.IO.Crypto.Common.PackFileEntry? entry = reader?.Files.FirstOrDefault(file =>
                string.Equals(file.Name, "table/specialabilitystringtable.xml", StringComparison.OrdinalIgnoreCase));
            if (reader == null || entry == null) {
                continue;
            }
            foreach (XmlNode node in reader.GetXmlDocument(entry).SelectNodes("ms2/key")!) {
                if (node is XmlElement row && int.TryParse(row.GetAttribute("id"), out int id) && row.GetAttribute("valueType") == "2") {
                    rates.Add(id);
                }
            }
            break;
        }
        return rates;
    }

    // A lap reward of a BlueMarble row's value1: <v round="5" itemID="20303095" count="10" />. The KR rows write the
    // amount as count (14 rows; none writes itemCount) and give no rank, so the item's own rank is used, as for the
    // board's item tiles. Round 0 (no round attribute) is the reward of every lap.
    public static BlueMarble.Round ParseBlueMarbleRound(XmlNode vNode) {
        if (!int.TryParse(vNode.Attributes?["round"]?.Value, out int round)) {
            round = 0;
        }
        if (!int.TryParse(vNode.Attributes?["itemID"]?.Value, out int itemId)) {
            itemId = 0;
        }
        if (!int.TryParse(vNode.Attributes?["count"]?.Value ?? vNode.Attributes?["itemCount"]?.Value, out int itemCount) || itemCount <= 0) {
            itemCount = 1;
        }
        return new BlueMarble.Round(
            RoundCount: round,
            Item: new ItemComponent(
                ItemId: itemId,
                Rarity: -1,
                Amount: itemCount,
                Tag: ItemTag.None));
    }

    private GameEventData? ParseGameEventData(GameEventType type, string value1, string value2, string value3, string value4) {
        var value1Xml = new XmlDocument();
        var value2Xml = new XmlDocument();
        var value3Xml = new XmlDocument();
        var value4Xml = new XmlDocument();

        switch (type) {
            case GameEventType.BlueMarble:
                if (!string.IsNullOrEmpty(value1)) {
                    value1Xml.LoadXml(value1);
                }

                value2Xml.LoadXml(value2);
                var rounds = new List<BlueMarble.Round>();
                var requiredItem = new ItemComponent(0, 0, 0, ItemTag.None);

                XmlNode? roundNode = value1Xml.FirstChild;
                if (roundNode != null) {
                    if (roundNode.Attributes?["consumeItemID"] != null) {
                        if (!int.TryParse(roundNode.Attributes?["consumeItemID"]?.Value, out int itemId)) {
                            itemId = 0;
                        }
                        if (!int.TryParse(roundNode.Attributes?["consumeItemCount"]?.Value, out int itemCount)) {
                            itemCount = 1;
                        }
                        requiredItem = new ItemComponent(itemId, -1, itemCount, ItemTag.None);
                    }

                    foreach (XmlNode vNode in roundNode.ChildNodes) {
                        rounds.Add(ParseBlueMarbleRound(vNode));
                    }
                }

                XmlNode? slotsNode = value2Xml.FirstChild;
                if (slotsNode == null) {
                    return null;
                }

                var slots = new List<BlueMarble.Slot>();
                foreach (XmlNode vNode in slotsNode.ChildNodes) {
                    if (!Enum.TryParse(vNode.Attributes?["type"]?.Value, true, out BlueMarbleSlotType slotType)) {
                        slotType = BlueMarbleSlotType.Item;
                    }

                    if (!int.TryParse(vNode.Attributes?["arg1"]?.Value, out int arg1)) {
                        arg1 = 0;
                    }

                    if (!int.TryParse(vNode.Attributes?["arg2"]?.Value, out int arg2)) {
                        arg2 = 0;
                    }

                    int moveAmount = 0;
                    if (slotType is BlueMarbleSlotType.Backward or BlueMarbleSlotType.Forward) {
                        moveAmount = arg1;
                    }

                    var blueMarbleSlotItem = new ItemComponent(0, 0, 0, ItemTag.None);
                    if (slotType is BlueMarbleSlotType.Item or BlueMarbleSlotType.Paradise) {
                        // TODO: Get rarity from item xmls
                        blueMarbleSlotItem = new ItemComponent(arg1, -1, arg2, ItemTag.None);
                    }

                    slots.Add(new BlueMarble.Slot(
                        Type: slotType,
                        MoveAmount: moveAmount,
                        Item: blueMarbleSlotItem));
                }
                return new BlueMarble(
                    RequiredItem: requiredItem,
                    Rounds: rounds.ToArray(),
                    Slots: slots.ToArray());
            case GameEventType.StringBoard:
                return new StringBoard(
                    Text: value4,
                    StringId: int.TryParse(value1, out int stringId) ? stringId : 0);
            case GameEventType.StringBoardLink:
                return new StringBoardLink(
                    Link: value1);
            case GameEventType.TrafficOptimizer:
                // values are hardcoded seeing as these are not shown in the table.
                return new TrafficOptimizer(
                    RideSyncInterval: 100,
                    UserSyncInterval: 100,
                    LinearMovementInterval: 100,
                    GuideObjectSyncInterval: 100);
            case GameEventType.LobbyMap:
                return new LobbyMap(
                    MapId: int.TryParse(value1, out int lobbyMapId) ? lobbyMapId : 0);
            case GameEventType.EventFieldPopup:
                return new EventFieldPopup(
                    MapId: int.TryParse(value1, out int fieldPopupMapId) ? fieldPopupMapId : 0);
            case GameEventType.SaleChat:
                return new SaleChat(
                    WorldChatDiscount: int.TryParse(value1, out int worldChatDiscount) ? worldChatDiscount : 0,
                    ChannelChatDiscount: int.TryParse(value2, out int channelChatDiscount) ? channelChatDiscount : 0);
            case GameEventType.AttendGift:
                value1Xml = new XmlDocument();
                value1Xml.LoadXml(value1);

                var rewards = new List<RewardItem>();
                if (value1Xml.FirstChild == null) {
                    return null;
                }
                foreach (XmlNode node in value1Xml.FirstChild.ChildNodes) {
                    if (!int.TryParse(node.Attributes?["itemID"]?.Value, out int itemId)) {
                        itemId = 0;
                    }
                    if (!int.TryParse(node.Attributes?["count"]?.Value, out int itemCount)) {
                        itemCount = 1;
                    }
                    if (!short.TryParse(node.Attributes?["grade"]?.Value, out short grade)) {
                        grade = -1;
                    }
                    rewards.Add(new RewardItem(itemId, grade, itemCount));
                }

                value2Xml = new XmlDocument();
                value2Xml.LoadXml(value2);
                if (value2Xml.FirstChild is not { Name: "ms2" }) {
                    return null;
                }

                XmlNode? stringNode = value2Xml.FirstChild.SelectSingleNode("string");
                XmlNode? configNode = value2Xml.FirstChild.SelectSingleNode("Config");
                XmlNode? payNode = value2Xml.FirstChild.SelectSingleNode("payAttend");
                // An event without a Config node asks no play time (AttendanceRules.AttendsAtGameStart): KR and NA have 16
                // such rows each (one the pay-attend row 170008), all ended, dropped before.
                if (stringNode == null) {
                    return null;
                }

                string name = AttendanceRules.TabName(stringNode.Attributes?["tabButton"]?.Value, stringNode.Attributes?["name"]?.Value);
                string mailTitle = stringNode.Attributes?["mailTitle"]?.Value ?? string.Empty;
                string mailContent = stringNode.Attributes?["mailContents"]?.Value ?? string.Empty;
                string link = stringNode.Attributes?["detailUrl"]?.Value ?? string.Empty;

                if (!int.TryParse(configNode?.Attributes?["requirePlaySeconds"]?.Value, out int requiredPlaySeconds)) {
                    requiredPlaySeconds = 0;
                }

                AttendGift.PayAttend? payAttend = payNode == null ? null : AttendanceRules.ParsePay(
                    payNode.Attributes?["type"]?.Value,
                    payNode.Attributes?["price"]?.Value,
                    payNode.Attributes?["maxCount"]?.Value,
                    payNode.Attributes?["couponID"]?.Value);

                AttendGift.Require? giftRequirement = null;
                if (!string.IsNullOrEmpty(value3)) {
                    value3Xml.LoadXml(value3);
                    if (value3Xml.FirstChild is { Name: "ms2" }) {
                        XmlNode? requirementNode = value3Xml.FirstChild.SelectSingleNode("require");
                        if (requirementNode != null) {
                            if (!Enum.TryParse(requirementNode.Attributes?["type"]?.Value, true, out AttendGiftRequirement requirement)) {
                                requirement = AttendGiftRequirement.None;
                            }

                            if (!int.TryParse(requirementNode.Attributes?["value1"]?.Value, out int requirementValue1)) {
                                requirementValue1 = 0;
                            }

                            if (!int.TryParse(requirementNode.Attributes?["value2"]?.Value, out int requirementValue2)) {
                                requirementValue2 = 0;
                            }

                            giftRequirement = new AttendGift.Require(
                                Type: requirement,
                                Value1: requirementValue1,
                                Value2: requirementValue2);
                        }
                    }
                }

                return new AttendGift(
                    Items: rewards.ToArray(),
                    Name: name,
                    MailTitle: mailTitle,
                    MailContent: mailContent,
                    Link: link,
                    RequiredPlaySeconds: requiredPlaySeconds,
                    Requirement: giftRequirement,
                    Pay: payAttend);
            case GameEventType.ReturnUser:
                var requiredTime = DateTimeOffset.MinValue;
                int daysInactive = 0;
                if (DateTime.TryParseExact(value1, "yyyy-MM-dd-HH-mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime returnUserTime)) {
                    requiredTime = new DateTimeOffset(returnUserTime);
                } else if (int.TryParse(value1, out daysInactive)) {
                }

                return new ReturnUser(
                    SeasonId: int.TryParse(value3, out int season) ? season : 0,
                    DateInactiveSince: requiredTime,
                    DaysInactive: daysInactive,
                    QuestIds: string.IsNullOrEmpty(value4) ? [] : value4.Split(',').Select(int.Parse).ToArray(),
                    RequiredLevel: int.TryParse(value1, out int levelRequirement) ? levelRequirement : 0,
                    RequiredUserValue: int.TryParse(value2, out int userValue) ? userValue : 0);
            case GameEventType.NewUser:
                var requiredNewUserTime = DateTimeOffset.MinValue;
                if (DateTime.TryParseExact(value1, "yyyy-MM-dd-HH-mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime newUserTime)) {
                    requiredNewUserTime = new DateTimeOffset(newUserTime);
                }

                return new NewUser(
                    SeasonId: int.TryParse(value2, out int newUserSeason) ? newUserSeason : 0,
                    DateCreatedBy: requiredNewUserTime);
            case GameEventType.ReturnUserCandidate:
                var unknownTime = DateTimeOffset.MinValue;
                if (DateTime.TryParseExact(value4, "yyyy-MM-dd-HH-mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime requiredDateTime)) {
                    unknownTime = new DateTimeOffset(requiredDateTime);
                }
                return new ReturnUserCandidate(
                    Season: int.TryParse(value3, out int returnUserCandidateSeason) ? returnUserCandidateSeason : 0,
                    SeasonId: int.TryParse(value2, out int returnUserCandidateSeasonId) ? returnUserCandidateSeasonId : 0,
                    MinLevel: int.TryParse(value1, out int returnUserCandidateMinLevel) ? returnUserCandidateMinLevel : 0,
                    UnknownDate: unknownTime);
            case GameEventType.ActiveUser:
                int[] value1Values = value1.Split(',').Select(int.Parse).ToArray();

                value2Xml.LoadXml(value2);
                int meret = 0;
                if (value2Xml.FirstChild is { Name: "money" }) {
                    foreach (XmlNode childNode in value2Xml.FirstChild.ChildNodes) {
                        if (childNode.Name == "v") {
                            string valueType = childNode.Attributes?["type"]?.Value ?? string.Empty;
                            if (valueType == "merat_e") {
                                string? amountStr = childNode.Attributes?["amount"]?.Value;
                                if (!int.TryParse(amountStr, out int meratEAmount)) {
                                    Console.WriteLine($"Failed to parse merat_e amount: {amountStr} for event type {type}");
                                } else {
                                    meret = meratEAmount;
                                }
                            }
                        }
                    }
                }
                return new ActiveUser(
                    MailId: value1Values[0],
                    MailDaysExpire: value1Values[1],
                    Meret: meret,
                    MinLevel: int.TryParse(value3, out int minLevel) ? minLevel : 0);
            case GameEventType.RPS:
                value1Xml = new XmlDocument();
                value1Xml.LoadXml(value1);
                if (value1Xml.FirstChild is not { Name: "ms2" }) {
                    return null;
                }

                XmlNode? rpseventNode = value1Xml.FirstChild.SelectSingleNode("rps_event");
                if (rpseventNode == null) {
                    return null;
                }

                int ticketId = 0;
                var rpsRewards = new List<Rps.RewardData>();
                foreach (XmlNode childNode in rpseventNode) {
                    if (childNode.Name == "gameTicket" && int.TryParse(childNode.Attributes?["itemID"]?.Value, out int itemId)) {
                        ticketId = itemId;
                    }

                    if (childNode.Name == "rewardData") {
                        if (!int.TryParse(childNode.Attributes?["condPlayCount"]?.Value, out int playCount)) {
                            playCount = 1;
                        }

                        // items
                        var rpsItems = new List<RewardItem>();
                        foreach (XmlNode itemNode in childNode.ChildNodes) {
                            foreach (XmlNode valueNode in itemNode.ChildNodes) {
                                if (!int.TryParse(valueNode.Attributes?["itemID"]?.Value, out int rpsRewardItemId)) {
                                    rpsRewardItemId = 0;
                                }

                                if (!short.TryParse(valueNode.Attributes?["grade"]?.Value, out short rpsRewardGrade)) {
                                    rpsRewardGrade = 1;
                                }

                                if (!int.TryParse(valueNode.Attributes?["count"]?.Value, out int rpsRewardCount)) {
                                    rpsRewardCount = 1;
                                }

                                rpsItems.Add(new RewardItem(rpsRewardItemId, rpsRewardGrade, rpsRewardCount));
                            }
                        }

                        rpsRewards.Add(new Rps.RewardData(
                            PlayCount: playCount,
                            Rewards: rpsItems.ToArray()));
                    }
                }

                return new Rps(
                    GameTicketId: ticketId,
                    Rewards: rpsRewards.ToArray(),
                    ActionsHtml: value2);
            case GameEventType.CoupleDance:
                return ReadCoupleDance(value1, value2);
            case GameEventType.DailyLoginReward:
                return ReadDailyLoginReward(value1, value2, value3);
            case GameEventType.LoginNotice:
                return new LoginNotice();
            case GameEventType.ItemBox:
                return ReadItemBox(value1);
            case GameEventType.Snowman:
                return ReadSnowman(value1, value2);
            case GameEventType.UserCondition:
                return new UserCondition(
                    Group: uint.TryParse(value1, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint group) ? (int) group : 0,
                    Url: value2);
            case GameEventType.Festival:
                // GameEventFestival::vfunc_2 reads value1 and value2 as unsigned numbers (FUN_140718200).
                return new Festival(
                    Value1: int.TryParse(value1, out int festivalValue1) ? festivalValue1 : 0,
                    UseType: int.TryParse(value2, out int festivalUseType) ? festivalUseType : 0);
            case GameEventType.FieldEffect:
                return new FieldEffect(
                    MapIds: value1.Split(',').Select(int.Parse).ToArray(),
                    Effect: value2);
            case GameEventType.WorldmapIcon:
                // value1 is sent as it is; GameEventWorldmapIconCli::OnStart parses it.
                return string.IsNullOrWhiteSpace(value1) ? null : new WorldmapIcon(Xml: value1);
            case GameEventType.DungeonExtraReward:
                return ReadDungeonExtraReward(value1);
            case GameEventType.reactor:
                // value1: field ids, value2: the placement count limit (GameEventReactor::ReadReactorData).
                return new Reactor(
                    MapIds: value1.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(mapId => int.TryParse(mapId, out int parsed) ? parsed : 0).Where(mapId => mapId > 0).ToArray(),
                    DailyCount: int.TryParse(value2, out int dailyCount) ? dailyCount : 0);
            case GameEventType.DTReward:
                string[] itemStrings = value1.Split(';');

                List<DTReward.Entry> items = [];
                foreach (string itemString in itemStrings) {
                    int[] itemData = itemString.Split(',').Select(int.Parse).ToArray();
                    items.Add(new DTReward.Entry(
                        StartDuration: itemData[0],
                        EndDuration: itemData[1],
                        MailContentId: itemData[2],
                        Item: new RewardItem(
                            itemId: itemData[3],
                            amount: itemData[4],
                            rarity: (short) itemData[5])));
                }

                return new DTReward(
                    Entries: items.ToArray());
            case GameEventType.ConstructShowItem:
                return new ConstructShowItem(
                    CategoryId: int.TryParse(value1, out int categoryId) ? categoryId : 0,
                    CategoryName: value2,
                    ItemIds: value4.Split(',').Select(int.Parse).ToArray());
            case GameEventType.MassiveConstructionEvent:
                return new MassiveConstructionEvent(
                    MapIds: value1.Split(',').Select(int.Parse).ToArray());
            case GameEventType.UGCMapContractSale:
                return new UGCMapContractSale(
                    DiscountAmount: int.TryParse(value1, out int contractSaleAmount) ? contractSaleAmount : 0);
            case GameEventType.UGCMapExtensionSale:
                return new UGCMapExtensionSale(
                    DiscountAmount: int.TryParse(value1, out int extensionSaleAmount) ? extensionSaleAmount : 0);
            case GameEventType.Gallery:
                value1Xml.LoadXml(value1);
                if (value1Xml.FirstChild is not { Name: "cards" }) {
                    return null;
                }

                var questIds = new List<int>();
                foreach (XmlNode valueNode in value1Xml.FirstChild) {
                    if (!int.TryParse(valueNode.Attributes?["quest"]?.Value, out int questId)) {
                        continue;
                    }
                    questIds.Add(questId);
                }

                value2Xml.LoadXml(value2);
                if (value2Xml.FirstChild is not { Name: "items" }) {
                    return null;
                }

                var galleryRewards = new List<RewardItem>();
                foreach (XmlNode itemNode in value2Xml.FirstChild) {
                    if (!int.TryParse(itemNode.Attributes?["itemID"]?.Value, out int itemId)) {
                        continue;
                    }

                    if (!short.TryParse(itemNode.Attributes?["grade"]?.Value, out short grade)) {
                        grade = 1;
                    }

                    if (!int.TryParse(itemNode.Attributes?["count"]?.Value, out int count)) {
                        count = 1;
                    }

                    galleryRewards.Add(new RewardItem(itemId, grade, count));
                }
                return new Gallery(
                    QuestIds: questIds.ToArray(),
                    RewardItems: galleryRewards.ToArray(),
                    RevealDayLimit: int.TryParse(value3, out int revealDayLimit) ? revealDayLimit : 1,
                    Image: value4);
            case GameEventType.BingoEvent:
                value1Xml.LoadXml(value1);
                if (value1Xml.FirstChild is not { Name: "ms2" }) {
                    return null;
                }

                var numbers = new List<int[]>();
                foreach (XmlNode childNode in value1Xml.FirstChild.ChildNodes) {
                    if (childNode.Name == "number") {
                        int[] dayNumbers = childNode.Attributes?["value"]?.Value.Split(',').Select(int.Parse).ToArray() ?? [];
                        numbers.Add(dayNumbers);
                    }
                }


                value2Xml.LoadXml(value2);
                if (value2Xml.FirstChild is not { Name: "ms2" }) {
                    return null;
                }


                var bingoRewards = new List<BingoEvent.BingoReward>();
                foreach (XmlNode childNode in value2Xml.FirstChild.ChildNodes) {
                    if (childNode.Name == "reward") {
                        var bingoItems = new List<RewardItem>();
                        foreach (XmlNode itemNode in childNode.ChildNodes) {
                            if (!int.TryParse(itemNode.Attributes?["itemID"]?.Value, out int itemId)) {
                                continue;
                            }

                            if (!short.TryParse(itemNode.Attributes?["grade"]?.Value, out short grade)) {
                                grade = 1;
                            }

                            if (!int.TryParse(itemNode.Attributes?["count"]?.Value, out int count)) {
                                count = 1;
                            }

                            bingoItems.Add(new RewardItem(itemId, grade, count));
                        }
                        bingoRewards.Add(new BingoEvent.BingoReward(
                            Items: bingoItems.ToArray()));
                    }
                }

                int pencilItemId = int.TryParse(value3, out int pencilId) ? pencilId : 0;
                int pencilPlusItemId = int.TryParse(value4, out int plusPencilId) ? plusPencilId : 0;
                return new BingoEvent(
                    Numbers: numbers.ToArray(),
                    Rewards: bingoRewards.ToArray(),
                    PencilItemId: pencilItemId,
                    PencilPlusItemId: pencilPlusItemId);
            case GameEventType.TimeRunEvent:
                value1Xml.LoadXml(value1);
                if (value1Xml.FirstChild is not { Name: "ms" }) {
                    return null;
                }

                var quests = new List<TimeRunEvent.Quest>();
                foreach (XmlNode childNode in value1Xml.FirstChild.ChildNodes) {
                    if (childNode.Name == "quest") {
                        if (!int.TryParse(childNode.Attributes?["questID"]?.Value, out int questId)) {
                            continue;
                        }
                        if (!int.TryParse(childNode.Attributes?["distance"]?.Value, out int distance)) {
                            continue;
                        }
                        if (!int.TryParse(childNode.Attributes?["openingDay"]?.Value, out int openingDay)) {
                            continue;
                        }
                        quests.Add(new TimeRunEvent.Quest(
                            Id: questId,
                            Distance: distance,
                            OpeningDay: openingDay));
                    }
                }

                value2Xml.LoadXml(value2);
                if (value2Xml.FirstChild is not { Name: "ms" }) {
                    return null;
                }
                // value2 holds the step rewards (<reward distance=..>, one per distance) and the final reward
                // (<final>), the rows GameEventTimeRunEvent reads. Every KR and NA row has only the final reward;
                // CN 700001 has four steps.
                (Dictionary<int, RewardItem> stepRewards, RewardItem? finalReward) = ReadTimeRunRewards(value2Xml.FirstChild);
                if (finalReward == null) {
                    return null;
                }

                if (!int.TryParse(value3, out int startTimeRunEventItemId)) {
                    return null;
                }
                return new TimeRunEvent(
                    StartItemId: startTimeRunEventItemId,
                    Quests: quests.ToArray(),
                    StepRewards: stepRewards,
                    FinalReward: finalReward.Value);
            case GameEventType.LongTermAttendGift:
                // Read only for the replay: with the switch off the type is not stored, as before.
                return replayAttendGift ? LongTermAttendGiftReader.Parse(value1, value2) : null;
            case GameEventType.StampEvent:
                // Read only for the replay: every row has ended, and with the switch off the type is not stored.
                return replayAttendGift ? StampEventReader.Parse(value1, value2, value3, value4) : null;
            case GameEventType.MapleSurvivalOpenPeriod:
                return new MapleSurvivalOpenPeriod();
            case GameEventType.GuildVsGameOpen:
                return new GuildVsGameOpen();
            case GameEventType.GuildChampionship:
                return new GuildChampionship();
            case GameEventType.FreeAirTaxi:
                return new FreeAirTaxi();
            case GameEventType.TransferItemBound:
                return EventSaleReader.TransferItemBound(value1);
            case GameEventType.ShutdownMapleSurvival:
                return new ShutdownMapleSurvival();
            case GameEventType.LevelUpPackage:
                // No item: the client's menu answers that the event is not valid.
                if (!int.TryParse(value1, out int packageItemId) || packageItemId == 0) {
                    return null;
                }
                return new LevelUpPackage(
                    ItemId: packageItemId,
                    QuestIds: value2);
            case GameEventType.DungeonEnterTimeLimit:
                if (string.IsNullOrEmpty(value1)) {
                    Console.WriteLine("DungeonEnterTimeLimit event without value1 skipped");
                    return null;
                }
                return new DungeonEnterTimeLimit(DungeonEnterTimeReader.Parse(value1));
            case GameEventType.DungeonOpenPeriod:
                // No tag: the client's event does nothing.
                return string.IsNullOrWhiteSpace(value1) ? null : new DungeonOpenPeriod(value1.Trim());
            case GameEventType.SaleUgcDesign:
                return EventSaleReader.UgcDesign(value1);
            case GameEventType.SaleHousing:
                return EventSaleReader.Housing(value1, value2, value3);
            case GameEventType.FindDungeonHelperBonus:
                return EventSaleReader.HelperBonus(value1);
            case GameEventType.SaleRemake:
                return EventSaleReader.Remake(value1, value2);
            case GameEventType.ExchangeScrollSale:
                return EventSaleReader.ExchangeScroll(value1, value2);
            case GameEventType.SaleBeautyShop:
                return EventSaleReader.BeautyShop(value1, value2, value3);
            case GameEventType.PremiumMarketSale:
                return EventSaleReader.PremiumMarket(value1, value2, value3);
            case GameEventType.DungeonBonusReward:
                return EventXmlReader.DungeonBonus(value1);
            case GameEventType.BurningLevelup:
                return EventXmlReader.BurningLevelup(value1, value2, value3, value4,
                    itemId => EventXmlReader.JumpingTargetLevel(ItemMapper.ItemMetadataById.GetValueOrDefault(itemId)));
            case GameEventType.QuestAdditionalReward:
                return EventXmlReader.QuestAdditionalReward(value1, value2, value3);
            case GameEventType.CollectItemGroup:
                return EventXmlReader.CollectItemGroup(value1);
            case GameEventType.ArcadeOpen:
                return EventXmlReader.ArcadeOpen(value1);
            case GameEventType.TreeWatering:
                return EventXmlReader.TreeWatering(value1, value2, value3);
            case GameEventType.MiniGameBonusReward:
                return EventXmlReader.MiniGameBonusReward(value1, value2);
            case GameEventType.SpawnNpc:
                return EventXmlReader.SpawnNpc(value1);
            case GameEventType.NpcShopShowItem:
                return EventSaleReader.ShopShowItem(value1, value2);
            case GameEventType.CharacterFirstConnect:
                return EventMailReader.FirstConnect(value1);
            case GameEventType.SendMail:
                return EventMailReader.SendMail(value1, value2, value3);
            case GameEventType.SAHotTime:
                specialRates ??= ClientSpecialRates();
                // Without a client pack every attribute counts as a rate: the 12 the KR rows name are rates.
                return EventSaleReader.HotTime(value1, value2, id => specialRates.Count == 0 || specialRates.Contains(id));
            case GameEventType.SaleAutoPlayInstrument:
                if (!int.TryParse(value1, out int performanceDiscount) && string.IsNullOrEmpty(value2)) {
                    return null;
                }
                return new SaleAutoPlayInstrument(
                    Discount: performanceDiscount,
                    ContentType: value2);
            case GameEventType.SaleAutoFishing:
                if (!int.TryParse(value1, out int fishingDiscount) && string.IsNullOrEmpty(value2)) {
                    return null;
                }
                return new SaleAutoFishing(
                    Discount: fishingDiscount,
                    ContentType: value2);
            case GameEventType.PetComposeSale:
            case GameEventType.SaleGemStoneUpgrade:
            case GameEventType.SaleLapenshardUpgradeRGB:
            case GameEventType.GuildSkillSale:
            case GameEventType.GuildPersonalSkillSale:
            case GameEventType.SaleEnchant:
            case GameEventType.GuildExp:
            case GameEventType.MasteryReduce:
            case GameEventType.AdventureLevelBonusExpRate:
                return EventSaleReader.Rate(value1);
            case GameEventType.PetMastery:
                return EventSaleReader.PetMastery(value1, value2);
            case GameEventType.BlackMarketFeeDiscount:
                return EventSaleReader.FeeRate(value1);
            default:
                return null;
        }
    }

    private OxQuizTable ParseOxQuizTable() {
        var results = new Dictionary<int, OxQuizTable.Entry>();
        foreach ((int id, OxQuiz quiz) in parser.ParseOxQuiz()) {
            results.Add(id, new OxQuizTable.Entry(
                Id: quiz.quizID,
                CategoryId: quiz.categoryID,
                Category: quiz.categoryStr,
                Question: quiz.quizStr,
                Level: quiz.level,
                IsTrue: quiz.answer,
                Answer: quiz.answerStr));
        }
        return new OxQuizTable(results);
    }

    // The step nodes of value2 in the client's step order: each has an optional <action anim=""/> and
    // <message value=""/>.
    private static readonly string[] CoupleDanceStepPaths = [
        "start", "play/ready", "play/select", "play/waitResult", "play/result/success", "play/result/fail", "end/normal", "end/good",
    ];

    // value1 <rewards> (the <daily> rewards by "order", then <goodEndBonus count="">) and value2 <gameData> (<dances>,
    // <start fieldID="">, <play maxRound="">, the steps), as CoupleDanceContext::LoadReward and LoadDance read them
    // (gameeventcontext.cpp). Null where those fail: a missing node, a daily or bonus without <items>, a step without
    // action and message; also when there is no dance or round, as the client then cannot play. Also when maxRound
    // or the goodEndBonus count do not fit the byte and the short the event is written with (GameEvent.WriteTo), as
    // the client would read another round count than mode 0x12 sends.
    public static CoupleDance? ReadCoupleDance(string value1, string value2) {
        XmlElement? rewards = LoadElement(value1, "rewards");
        XmlElement? gameData = LoadElement(value2, "gameData");
        if (rewards == null || gameData == null) {
            return null;
        }

        // By order; the first <daily> of an order wins (std::map insert).
        var daily = new SortedDictionary<int, RewardItem[]>();
        foreach (XmlElement node in rewards.SelectNodes("daily")?.OfType<XmlElement>() ?? []) {
            int order = int.TryParse(node.GetAttribute("order"), out int parsedOrder) ? parsedOrder : 0;
            RewardItem[]? items = ReadCoupleDanceItems(node);
            if (items == null) {
                return null;
            }
            daily.TryAdd(order, items);
        }
        if (rewards.SelectSingleNode("goodEndBonus") is not XmlElement goodEnd || ReadCoupleDanceItems(goodEnd) is not { } goodEndItems) {
            return null;
        }
        int goodEndCount = int.TryParse(goodEnd.GetAttribute("count"), out int parsedCount) ? parsedCount : 0;

        if (gameData.SelectSingleNode("dances") is not XmlElement dancesNode
            || gameData.SelectSingleNode("play") is not XmlElement play
            || gameData.SelectSingleNode("start") is not XmlElement start) {
            return null;
        }
        // Every child element of <dances> is a dance.
        string[] dances = dancesNode.ChildNodes.OfType<XmlElement>().Select(node => node.GetAttribute("anim")).ToArray();
        int maxRound = int.TryParse(play.GetAttribute("maxRound"), out int parsedRound) ? parsedRound : 0;
        int fieldId = int.TryParse(start.GetAttribute("fieldID"), out int parsedField) ? parsedField : 0;
        if (dances.Length == 0 || maxRound <= 0 || maxRound > byte.MaxValue || goodEndCount is < 0 or > short.MaxValue) {
            return null;
        }

        var steps = new List<CoupleDance.Step>();
        foreach (string path in CoupleDanceStepPaths) {
            if (gameData.SelectSingleNode(path) is not { } node) {
                return null;
            }
            string action = (node.SelectSingleNode("action") as XmlElement)?.GetAttribute("anim") ?? string.Empty;
            string message = (node.SelectSingleNode("message") as XmlElement)?.GetAttribute("value") ?? string.Empty;
            if (action.Length == 0 && message.Length == 0) {
                return null;
            }
            steps.Add(new CoupleDance.Step(action, message));
        }

        return new CoupleDance(
            DailyRewards: daily.Values.ToArray(),
            GoodEndCount: goodEndCount,
            GoodEndRewards: goodEndItems,
            Dances: dances,
            MaxRound: maxRound,
            FieldId: fieldId,
            Steps: steps.ToArray());
    }

    // GameEventItemBox::vfunc_2 loads value1 and gives its first element, whatever its name, to
    // ItemBoxEventContext::Load: fieldID and boxItemID as unsigned numbers, bannerPath, title and desc as text.
    // Null without that element, or without a field or a box item, as the dialog then has no box to offer.
    public static ItemBox? ReadItemBox(string value1) {
        if (string.IsNullOrWhiteSpace(value1)) {
            return null;
        }
        var document = new XmlDocument();
        try {
            document.LoadXml(value1);
        } catch (XmlException) {
            return null;
        }
        if (document.DocumentElement is not { } root) {
            return null;
        }
        int fieldId = int.TryParse(root.GetAttribute("fieldID"), out int parsedField) ? parsedField : 0;
        int boxItemId = int.TryParse(root.GetAttribute("boxItemID"), out int parsedItem) ? parsedItem : 0;
        if (fieldId <= 0 || boxItemId <= 0) {
            return null;
        }
        return new ItemBox(
            FieldId: fieldId,
            BoxItemId: boxItemId,
            Title: root.GetAttribute("title"),
            Desc: root.GetAttribute("desc"),
            BannerPath: root.GetAttribute("bannerPath"));
    }

    // GameEventSnowman::vfunc_2 (gameevent.cpp) loads value1: the root's <option thema=""/> (none: no thema) and each
    // <snowman>'s <condition snowflakeCount dailyMaxCount accumMaxCount/> and the <v itemID grade count/> of its
    // <rewardItems> and <accumRewardItems>. A snowman without one of those nodes fails ReadSnowmanData and the
    // event is not loaded; so is one without a snowman, as the dialog then has nothing to show. value2 is kept as
    // it is: the event sends it with the snowmen (vfunc_3), and the dialog reads its layout from it.
    public static Snowman? ReadSnowman(string value1, string value2) {
        XmlElement? root = LoadElement(value1, "ms2");
        if (root == null) {
            return null;
        }
        string theme = (root.SelectSingleNode("option") as XmlElement)?.GetAttribute("thema") ?? string.Empty;
        var snowmen = new List<Snowman.Entry>();
        foreach (XmlElement node in root.SelectNodes("snowman")?.OfType<XmlElement>() ?? []) {
            if (node.SelectSingleNode("condition") is not XmlElement condition
                || node.SelectSingleNode("rewardItems") is not XmlElement rewards
                || node.SelectSingleNode("accumRewardItems") is not XmlElement accumRewards) {
                return null;
            }
            snowmen.Add(new Snowman.Entry(
                Goal: int.TryParse(condition.GetAttribute("snowflakeCount"), out int goal) ? goal : 0,
                DailyMax: int.TryParse(condition.GetAttribute("dailyMaxCount"), out int dailyMax) ? dailyMax : 0,
                AccumMax: int.TryParse(condition.GetAttribute("accumMaxCount"), out int accumMax) ? accumMax : 0,
                Rewards: ReadSnowmanItems(rewards),
                AccumRewards: ReadSnowmanItems(accumRewards)));
        }
        if (snowmen.Count == 0 || snowmen.Count > short.MaxValue) {
            return null;
        }
        return new Snowman(theme, snowmen.ToArray(), value2 ?? string.Empty, []);
    }

    // ItemEntity::vfunc_2 per <v>: itemID, grade (rank) and count.
    private static RewardItem[] ReadSnowmanItems(XmlElement node) {
        var items = new List<RewardItem>();
        foreach (XmlElement value in node.SelectNodes("v")?.OfType<XmlElement>() ?? []) {
            if (!int.TryParse(value.GetAttribute("itemID"), out int itemId) || itemId == 0) {
                continue;
            }
            short grade = short.TryParse(value.GetAttribute("grade"), out short parsedGrade) ? parsedGrade : (short) 1;
            int count = int.TryParse(value.GetAttribute("count"), out int parsedCount) ? parsedCount : 1;
            items.Add(new RewardItem(itemId, grade, count));
        }
        return items.ToArray();
    }

    // The snowflake sources of a snowman event: the server table's promotionContents rows of promotionType Snowman
    // (the locale's and the feature's, as the other server tables) whose startTime and endTime are the event's.
    // Neither table names the other; the KR rows of each snowman event run over its exact dates.
    private Snowman.Source[] SnowmanSources(string eventStart, string eventEnd) {
        Maple2.File.IO.Crypto.Common.PackFileEntry? entry = xmlReader.Files.FirstOrDefault(file =>
            file.Name.Equals("table/Server/promotionContents.xml", StringComparison.OrdinalIgnoreCase));
        if (entry == null) {
            return [];
        }
        Snowman.Source[] sources = MapSnowmanSources(xmlReader.GetXmlDocument(entry), eventStart, eventEnd,
            row => FeatureLocaleFilter.FeatureEnabled(row.GetAttribute("feature")) && FeatureLocaleFilter.HasLocale(row.GetAttribute("locale")));
        Console.WriteLine($"Snowman {eventStart}..{eventEnd}: {sources.Length} snowflake sources");
        return sources;
    }

    // Rows of a type the server does not count (not a ConditionType name, such as the 2019 rows' harvest, gathering,
    // manufacturing and playinstrument_time, the last of which no handler counts) are left out, as are rows
    // without a point or a chance. Rows that repeat one type, point and chance (the KR file lists the 2019 rows
    // twice) are one source.
    public static Snowman.Source[] MapSnowmanSources(XmlDocument document, string eventStart, string eventEnd, Func<XmlElement, bool> available) {
        var sources = new List<Snowman.Source>();
        foreach (XmlElement row in document.SelectNodes("ms2/promotion")?.OfType<XmlElement>() ?? []) {
            if (row.GetAttribute("promotionType") != "Snowman" || !available(row)
                || row.GetAttribute("startTime") != eventStart || row.GetAttribute("endTime") != eventEnd) {
                continue;
            }
            if (!Enum.TryParse(row.GetAttribute("type"), out Maple2.Model.Enum.ConditionType type) || !Enum.IsDefined(type)
                || type is Maple2.Model.Enum.ConditionType.playinstrument_time
                || !int.TryParse(row.GetAttribute("point"), out int point) || point <= 0
                || !int.TryParse(row.GetAttribute("prop"), out int prop) || prop <= 0) {
                continue;
            }
            var source = new Snowman.Source(type, point, Math.Min(prop, 10000));
            if (!sources.Contains(source)) {
                sources.Add(source);
            }
        }
        return sources.ToArray();
    }

    private static XmlElement? LoadElement(string value, string name) {
        if (string.IsNullOrWhiteSpace(value)) {
            return null;
        }
        var document = new XmlDocument();
        try {
            document.LoadXml(value);
        } catch (XmlException) {
            return null;
        }
        return document.DocumentElement is { } root && root.Name == name ? root : null;
    }

    // <items><v itemID="" grade="" count=""/></items> under the node; null without <items>.
    private static RewardItem[]? ReadCoupleDanceItems(XmlNode node) {
        if (node.SelectSingleNode("items") == null) {
            return null;
        }
        var items = new List<RewardItem>();
        foreach (XmlElement value in node.SelectNodes("items/v")?.OfType<XmlElement>() ?? []) {
            if (!int.TryParse(value.GetAttribute("itemID"), out int itemId) || itemId == 0) {
                continue;
            }
            short grade = short.TryParse(value.GetAttribute("grade"), out short parsedGrade) ? parsedGrade : (short) 1;
            int count = int.TryParse(value.GetAttribute("count"), out int parsedAmount) ? parsedAmount : 1;
            items.Add(new RewardItem(itemId, grade, count));
        }
        return items.ToArray();
    }

    // The old hand copies: materials 37000056..63 taken to use table 37000055. A material with a table id from
    // the played client is asked by that id first (ItemMergeManager.MergeSlot), so a copy only serves a material
    // that got none. An id with rows of its own keeps them.
    // GameEventDungeonExtraReward::vfunc_2 (gameevent.cpp:1867): each child of value1's root is a room's count
    // (DungeonExtraRewardCountCtx::Load); one without a room or a count is skipped, and a later row of a room
    // replaces the earlier one (CAtlMap::SetAt). No row: no event.
    public static DungeonExtraReward? ReadDungeonExtraReward(string value1) {
        if (string.IsNullOrWhiteSpace(value1)) {
            return null;
        }
        var document = new XmlDocument();
        try {
            document.LoadXml(value1);
        } catch (XmlException) {
            return null;
        }
        if (document.DocumentElement == null) {
            return null;
        }

        var entries = new Dictionary<int, DungeonExtraReward.Entry>();
        foreach (XmlNode node in document.DocumentElement.ChildNodes) {
            if (node is not XmlElement element) {
                continue;
            }
            int dungeonId = int.TryParse(element.GetAttribute("dungeonID"), out int parsedId) ? parsedId : 0;
            int count = int.TryParse(element.GetAttribute("count"), out int parsedCount) ? parsedCount : 0;
            if (dungeonId == 0 || count == 0) {
                continue;
            }
            // Parse_1402199e0: the names without case, else none.
            Maple2.Model.Enum.DungeonCooldownType cooldownType = element.GetAttribute("cooldownType").ToLowerInvariant() switch {
                "dayofweeks" => Maple2.Model.Enum.DungeonCooldownType.dayOfWeeks,
                "nextday" => Maple2.Model.Enum.DungeonCooldownType.nextDay,
                _ => Maple2.Model.Enum.DungeonCooldownType.none,
            };
            int cooldownValue = int.TryParse(element.GetAttribute("cooldownValue"), out int parsedValue) ? parsedValue : 0;
            entries[dungeonId] = new DungeonExtraReward.Entry(dungeonId, count, cooldownType, cooldownValue);
        }
        return entries.Count == 0 ? null : new DungeonExtraReward(entries.Values.ToArray());
    }

    public static void AddHandCopies(Dictionary<int, Dictionary<int, ItemMergeTable.Entry>> results) {
        if (!results.TryGetValue(37000055, out Dictionary<int, ItemMergeTable.Entry>? rows)) {
            return;
        }
        for (int i = 37000056; i < 37000064; i++) {
            results.TryAdd(i, rows);
        }
    }

    // A merge slot's part as the client keys it. The client reads a slot without the part attribute as part 0
    // (CItemMergeOptionData's constructor sets m_nId 0, Load reads "part" over it, itemmergeoptiontable.cpp:281
    // and :295) and asks part 0 for a material whose subtype is not 1 (FindMergeData, :674). The parser reads
    // a missing part as 1. No client slot has part 1 and no item has equip subtype 1 (0, or 12 to 83), so a 1
    // is a slot without part.
    public static int MergePart(int parsedPart) {
        return parsedPart == 1 ? 0 : parsedPart;
    }

    private ItemMergeTable ParseItemMergeOptionTable() {
        var results = new Dictionary<int, Dictionary<int, ItemMergeTable.Entry>>();
        Dictionary<(int Id, int Part), int[]> partLimits = ClientPartLimits();
        foreach ((int id, MergeOption mergeOption) in parser.ParseItemMergeOption()) {
            var slots = new Dictionary<int, ItemMergeTable.Entry>();
            foreach (MergeOption.Slot slotEntry in mergeOption.slot) {
                var ingredients = new List<ItemComponent>();

                ItemComponent? ingredient1 = ParseItemMaterial(slotEntry.itemMaterial1);
                if (ingredient1 != null) {
                    ingredients.Add(ingredient1);
                }
                ItemComponent? ingredient2 = ParseItemMaterial(slotEntry.itemMaterial2);
                if (ingredient2 != null) {
                    ingredients.Add(ingredient2);
                }

                var basicOptions = new Dictionary<BasicAttribute, ItemMergeTable.Option>();
                var specialOptions = new Dictionary<SpecialAttribute, ItemMergeTable.Option>();

                foreach (MergeOption.Option mergeOptionEntry in slotEntry.option) {
                    if (mergeOptionEntry.optionName is "str" or "dex" or "int" or "luk" or "hp" or "hp_rgp" or "hp_inv" or "sp" or "sp_rgp" or "sp_inv" or "ep" or "ep_rgp" or "ep_inv" or "asp" or "msp" or "atp" or "evp" or
                        "cap" or "cad" or "car" or "ndd" or "abp" or "jmp" or "pap" or "map" or "par" or "mar" or "wapmin" or "wapmax" or "dmg" or "pen" or "rmsp" or "bap" or "bap_pet") {
                        var basicAttribute = mergeOptionEntry.optionName.ToBasicAttribute();
                        List<ItemMergeTable.Range<int>> values = [];
                        List<ItemMergeTable.Range<int>> rates = [];
                        List<int> weights = [];
                        int min = mergeOptionEntry.min;
                        if (basicAttribute is BasicAttribute.Piercing or BasicAttribute.PerfectGuard or
                            BasicAttribute.JumpHeight) {
                            // Looping by 10 because that's the max amount of values in the xml
                            for (int i = 0; i < 10; i++) {
                                (int value, int weight) = mergeOptionEntry[i];
                                if (value == 0) {
                                    continue;
                                }
                                rates.Add(new ItemMergeTable.Range<int>(min + 1, value));
                                values.Add(new ItemMergeTable.Range<int>(0, 0));
                                weights.Add(weight);
                                min = value;
                            }
                        } else {
                            for (int i = 0; i < 10; i++) {
                                (int value, int weight) = mergeOptionEntry[i];
                                if (value == 0) {
                                    continue;
                                }
                                values.Add(new ItemMergeTable.Range<int>(min + 1, value));
                                rates.Add(new ItemMergeTable.Range<int>(0, 0));
                                weights.Add(weight);
                                min = value;
                            }
                        }

                        basicOptions[basicAttribute] = new ItemMergeTable.Option(
                            Values: values.ToArray(),
                            Rates: rates.ToArray(),
                            Weights: weights.ToArray());
                    } else {
                        var specialAttribute = mergeOptionEntry.optionName.ToSpecialAttribute();
                        List<ItemMergeTable.Range<int>> values = [];
                        List<ItemMergeTable.Range<int>> rates = [];
                        List<int> weights = [];
                        int min = mergeOptionEntry.min;
                        if (specialAttribute is SpecialAttribute.HpOnKill or SpecialAttribute.ReduceCooldown or SpecialAttribute.ReduceKnockBack or SpecialAttribute.MassiveOxSpeed or SpecialAttribute.MassiveTrapMasterSpeed or
                            SpecialAttribute.MassiveFinalSurvivalSpeed or SpecialAttribute.MassiveCrazyRunnerSpeed or SpecialAttribute.MassiveShCrazyRunnerSpeed or SpecialAttribute.MassiveEscapeSpeed or SpecialAttribute.MassiveSpringBeachSpeed or
                            SpecialAttribute.MassiveDanceDanceSpeed or SpecialAttribute.DarkStreamEvp or SpecialAttribute.CompleteFieldMissionSpeed or SpecialAttribute.AdditionalEffect95000018 or SpecialAttribute.AdditionalEffect95000012 or
                            SpecialAttribute.AdditionalEffect95000014 or SpecialAttribute.AdditionalEffect95000020 or SpecialAttribute.AdditionalEffect95000021 or SpecialAttribute.AdditionalEffect95000022 or SpecialAttribute.AdditionalEffect95000023
                            or SpecialAttribute.AdditionalEffect95000024 or SpecialAttribute.AdditionalEffect95000025 or SpecialAttribute.AdditionalEffect95000026 or SpecialAttribute.AdditionalEffect95000027 or SpecialAttribute.AdditionalEffect95000028 or
                            SpecialAttribute.AdditionalEffect95000029 or SpecialAttribute.DashDistance or SpecialAttribute.SpiritOnKill or SpecialAttribute.StaminaOnKill or SpecialAttribute.PvpDamage or SpecialAttribute.ReducePvpDamage or SpecialAttribute.SkillLevelUpTier1
                            or SpecialAttribute.SkillLevelUpTier2 or SpecialAttribute.SkillLevelUpTier3 or SpecialAttribute.SkillLevelUpTier4 or SpecialAttribute.SkillLevelUpTier5 or SpecialAttribute.SkillLevelUpTier6 or SpecialAttribute.SkillLevelUpTier7 or SpecialAttribute.SkillLevelUpTier8
                            or SpecialAttribute.SkillLevelUpTier9 or SpecialAttribute.SkillLevelUpTier10 or SpecialAttribute.SkillLevelUpTier11 or SpecialAttribute.SkillLevelUpTier12 or SpecialAttribute.SkillLevelUpTier13 or SpecialAttribute.SkillLevelUpTier14 or SpecialAttribute.ChaosRaidAttackSpeed
                            or SpecialAttribute.ChaosRaidAccuracy or SpecialAttribute.ChaosRaidHp or SpecialAttribute.PetTrapReward) {
                            for (int i = 0; i < 10; i++) {
                                (int value, int weight) = mergeOptionEntry[i];
                                if (value == 0) {
                                    continue;
                                }
                                values.Add(new ItemMergeTable.Range<int>(min + 1, value));
                                rates.Add(new ItemMergeTable.Range<int>(0, 0));
                                weights.Add(weight);
                                min = value;
                            }
                        } else {
                            for (int i = 0; i < 10; i++) {
                                (int value, int weight) = mergeOptionEntry[i];
                                if (value == 0) {
                                    continue;
                                }
                                rates.Add(new ItemMergeTable.Range<int>(min + 1, value));
                                values.Add(new ItemMergeTable.Range<int>(0, 0));
                                weights.Add(weight);
                                min = value;
                            }
                        }

                        specialOptions[specialAttribute] = new ItemMergeTable.Option(
                            Values: values.ToArray(),
                            Rates: rates.ToArray(),
                            Weights: weights.ToArray());
                    }
                }
                int part = MergePart(slotEntry.part);
                var slot = new ItemMergeTable.Entry(
                    Slot: part,
                    MesoCost: slotEntry.consumeMeso,
                    Materials: ingredients.ToArray(),
                    BasicOptions: basicOptions,
                    SpecialOptions: specialOptions
                ) {
                    PartLimit = partLimits.GetValueOrDefault((id, part)),
                };

                slots.Add(part, slot);
            }
            results.Add(id, slots);
        }
        Dictionary<int, int>? tableIds = PlayedTableIds();
        AddHandCopies(results);
        return new ItemMergeTable(results) { TableIds = tableIds };

        // The table id of a merge material is its property param1 (CItemData::GetMergeOptionId), which the item
        // parser does not read. The played client's pack keeps its items in itemdata/*.xml; the property nodes
        // of type 19 there give material id -> param1. An id with two different values is left out. Null
        // without that pack, when it has no such files, or when none could be read.
        Dictionary<int, int>? PlayedTableIds() {
            if (playedReader == null) {
                return null;
            }
            var ids = new Dictionary<int, int>();
            var twice = new HashSet<int>();
            try {
                foreach (Maple2.File.IO.Crypto.Common.PackFileEntry entry in playedReader.Files.Where(file => file.Name.StartsWith("itemdata/"))) {
                    foreach (XmlNode node in playedReader.GetXmlDocument(entry).SelectNodes("//property[@type='19']")!) {
                        if (node is not XmlElement property || !int.TryParse(property.GetAttribute("param1"), out int tableId) || tableId == 0) continue;
                        XmlNode? item = property.ParentNode;
                        while (item is XmlElement element && !element.HasAttribute("id")) {
                            item = item.ParentNode;
                        }
                        if (item is not XmlElement owner || !int.TryParse(owner.GetAttribute("id"), out int itemId)) continue;
                        if (ids.TryGetValue(itemId, out int known) && known != tableId) {
                            twice.Add(itemId);
                        }
                        ids[itemId] = tableId;
                    }
                }
            } catch (Exception ex) {
                Console.WriteLine($"item merge: the played client's material table ids were not read ({ex.GetType().Name}: {ex.Message})");
                return null;
            }
            foreach (int itemId in twice) {
                ids.Remove(itemId);
            }
            Console.WriteLine($"item merge: {ids.Count} material table ids from the played client's item data");
            return ids.Count == 0 ? null : ids;
        }

        // The server pack's table has no partLimit; the client's Table/itemMergeOptionBase.xml has it per
        // (mergeOption id, slot part). The data folder's pack first, then the played client's, whose row wins.
        // Without a client pack, or with one this parser cannot read, the rows carry no list.
        Dictionary<(int Id, int Part), int[]> ClientPartLimits() {
            var limits = new Dictionary<(int Id, int Part), int[]>();
            foreach (M2dReader? reader in new[] { clientReader, playedReader }) {
                if (reader == null) {
                    continue;
                }
                try {
                    foreach (MergeOptionNew option in new ItemOptionParser(reader).ParseMergeOptionBaseNew()) {
                        foreach (Slot slot in option.slot) {
                            limits[(option.id, MergePart(slot.part))] = slot.partLimit ?? [];
                        }
                    }
                } catch (Exception ex) {
                    Console.WriteLine($"item merge: the client's partLimit lists were not read ({ex.GetType().Name}: {ex.Message})");
                }
            }
            return limits;
        }

        ItemComponent? ParseItemMaterial(string[] itemMaterial) {
            var tag = ItemTag.None;
            if (itemMaterial.Length > 0) {
                string[] item = itemMaterial[0].Split(':');
                if (item.Length == 2) {
                    tag = Enum.TryParse(item[1], out ItemTag itemTag) ? itemTag : ItemTag.None;
                }
                int itemId = int.TryParse(item[0], out int id) ? id : 0;
                int rarity = int.TryParse(itemMaterial[1], out int r) ? r : 1;
                int amount = int.TryParse(itemMaterial[2], out int a) ? a : 1;
                if (tag != ItemTag.None || itemId > 0) {
                    return new ItemComponent(itemId, rarity, amount, tag);
                }
            }
            return null;
        }
    }

    private ShopTable ParseShop() {
        var results = new Dictionary<int, ShopMetadata>();
        foreach ((int shopId, ShopGameInfo shopInfo) in parser.ParseShopGameInfo()) {
            results.Add(shopId, MapShop(shopInfo));
        }
        return new ShopTable(results);
    }

    public static ShopMetadata MapShop(ShopGameInfo shopInfo) {
        return new ShopMetadata(
            Id: shopInfo.shopID,
            CategoryId: shopInfo.categoryID,
            Name: shopInfo.iconName,
            FrameType: (ShopFrameType) shopInfo.uiFrameType,
            DisplayOnlyUsable: shopInfo.showOnlyUsableItem,
            HideStats: shopInfo.hideOptionInfo,
            DisplayProbability: shopInfo.showProbInfo,
            IsOnlySell: shopInfo.isOnlySell,
            OpenWallet: shopInfo.isOpenTokenPocket,
            DisplayNew: false, // this isn't present in the table
            DisableDisplayOrderSort: shopInfo.disableDisplayOrderSort,
            RestockTime: string.IsNullOrEmpty(shopInfo.resetFixedTime) ? 0 : DateTime.ParseExact(shopInfo.resetFixedTime, "yyyy-MM-dd-HH-mm-ss", CultureInfo.InvariantCulture).ToEpochSeconds(),
            EnableReset: shopInfo.resetEnable,
            RestockData: new ShopRestockData(
                ResetType: (ResetType) shopInfo.resetType,
                CurrencyType: (ShopCurrencyType) shopInfo.resetPaymentType,
                ExcessCurrencyType: (ShopCurrencyType) shopInfo.resetPaymentType, // not present in the table, using resetPaymentType for now
                MinItemCount: shopInfo.resetListMin,
                MaxItemCount: shopInfo.resetListMax,
                Price: shopInfo.resetPrice,
                // The client reads it as useLuaResetPayment (npcshopdata) and, with it set, shows the tiered
                // reset tooltip (Shop.RestockCost). Before, it was always off. No KR shop sets it.
                EnablePriceMultiplier: shopInfo.useLuaResetPayment,
                DisableInstantRestock: shopInfo.resetButtonHide,
                AccountWide: shopInfo.resetByAccount)
        );
    }

    private ShopItemTable ParseShopItems() {
        var results = new Dictionary<int, Dictionary<int, ShopItemMetadata>>();
        HashSet<int>? known = PlayedItemIds();
        var count = new PlayedClientItems.Count();
        foreach ((int shopId, ShopGame data) in parser.ParseShopGame()) {
            var shopResults = new Dictionary<int, ShopItemMetadata>();
            foreach (ShopGame.Item item in data.item) {
                // Check if item exists in ItemMetadataById
                if (!ItemMapper.ItemMetadataById.TryGetValue(item.id, out ItemMetadata? itemMeta)) {
                    continue;
                }
                if (known != null && !PlayedClientItems.KnownShopItem(item.id, item.paymentItemID, known, ref count)) {
                    continue;
                }

                string[] achievementArray = string.IsNullOrEmpty(item.requireAchieve) ? [] : item.requireAchieve.Split(",");
                int achievementId = 0;
                int achievementRank = 0;
                if (achievementArray.Length == 2) {
                    if (!int.TryParse(achievementArray[0], out achievementId)) {
                        achievementId = 0;
                    }
                    if (!int.TryParse(achievementArray[1], out achievementRank)) {
                        achievementRank = 1;
                    }
                }

                byte championshipRank = 0;
                short championShipJoinCount = 0;
                if (item.requireChampionshipInfo.Length == 2) {
                    championshipRank = (byte) item.requireChampionshipInfo[0];
                    championShipJoinCount = (short) item.requireChampionshipInfo[1];
                }

                var npcType = GuildNpcType.Unknown;
                short guildNpcLevel = 0;
                if (item.requireGuildNpc.Length == 2) {
                    npcType = item.requireGuildNpc[0] switch {
                        "goods" => GuildNpcType.Goods,
                        "equip" => GuildNpcType.Equip,
                        "gemstone" => GuildNpcType.Gemstone,
                        "itemMerge" => GuildNpcType.ItemMerge,
                        "music" => GuildNpcType.Music,
                        "quest" => GuildNpcType.Quest,
                        _ => GuildNpcType.Unknown,
                    };
                    if (short.TryParse(item.requireGuildNpc[1], out short level)) {
                        guildNpcLevel = level;
                    }
                }

                RestrictedBuyData? restrictedBuyData = null;
                // The client keeps a buy period when any of the dates, the part times or the days is given.
                if (!string.IsNullOrEmpty(item.startDate) || !string.IsNullOrEmpty(item.endDate) || item.partTime.Length > 0 || item.dayOfWeek.Length > 0) {
                    var buyTimeOfDays = new List<BuyTimeOfDay>();
                    foreach (string partTime in item.partTime) {
                        (TimeSpan startPartTime, TimeSpan endPartTime) = ParsePartTime(partTime);
                        buyTimeOfDays.Add(new BuyTimeOfDay((int) startPartTime.TotalSeconds, (int) endPartTime.TotalSeconds));
                    }
                    restrictedBuyData = new RestrictedBuyData {
                        Days = item.dayOfWeek.Length == 0 ? [] : Array.ConvertAll(item.dayOfWeek, day => (ShopBuyDay) day),
                        TimeRanges = buyTimeOfDays,
                        StartTime = string.IsNullOrEmpty(item.startDate) ? 0 : DateTime.ParseExact(item.startDate, "yyyy-MM-dd-HH-mm-ss", CultureInfo.InvariantCulture).ToEpochSeconds(),
                        EndTime = string.IsNullOrEmpty(item.endDate) ? 0 : DateTime.ParseExact(item.endDate, "yyyy-MM-dd-HH-mm-ss", CultureInfo.InvariantCulture).ToEpochSeconds()
                    };
                }

                var entry = new ShopItemMetadata(
                    Id: item.sn,
                    ShopId: shopId,
                    ItemId: item.id,
                    Rarity: (byte) item.grade,
                    Cost: new ShopCost {
                        Amount = (int) item.price,
                        ItemId = item.paymentItemID,
                        SaleAmount = 0, // ?
                        Type = (ShopCurrencyType) item.paymentType,
                    },
                    SellCount: item.sellCount,
                    Category: item.category,
                    Requirements: new ShopItemMetadata.Requirement(
                        GuildTrophy: item.requireGuildTrophy,
                        Achievement: new ShopItemMetadata.Achievement(
                            Id: achievementId,
                            Rank: achievementRank),
                        Championship: new ShopItemMetadata.Championship(
                            Rank: championshipRank,
                            JoinCount: championShipJoinCount),
                        GuildNpc: new ShopItemMetadata.GuildNpc(
                            Type: npcType,
                            Level: guildNpcLevel),
                        QuestAlliance: new ShopItemMetadata.QuestAlliance(
                            // The client's fame alliance names (Parse_14021a330), as the fame table reads them.
                            Type: TableMapper.ParseAlliance(item.requireAlliance ?? string.Empty),
                            Grade: item.requireAllianceGrade)),
                    RestrictedBuyData: restrictedBuyData,
                    SellUnit: (short) item.sellUnit,
                    SaleTag: (ShopItemLabel) item.saleTag,
                    IconTag: item.paymentIconTag,
                    WearForPreview: item.wearForPreview,
                    RandomOption: item.randomOption,
                    Probability: item.prob,
                    IsPremiumItem: item.premiumItem,
                    FrameType: (byte) item.frameType);
                shopResults.Add(item.sn, entry);
            }
            results.Add(shopId, shopResults);
        }
        if (known != null) {
            Console.WriteLine($"shop_game: {count} (items not in the played client's item data)");
        }
        return new ShopItemTable(results);
    }

    private BeautyShopTable ParseBeautyShops() {
        var results = new Dictionary<int, BeautyShopMetadata>();
        results = MergeBeautyShopData(results, parser.ParseShopBeauty());
        results = MergeBeautyShopData(results, parser.ParseShopBeautyCoupon());
        results = MergeBeautyShopData(results, parser.ParseShopBeautySpecialHair());
        return new BeautyShopTable(results);
    }

    private Dictionary<int, BeautyShopMetadata> MergeBeautyShopData(Dictionary<int, BeautyShopMetadata> entries, IEnumerable<(int, ShopBeauty)> beautyParser) {
        foreach ((int shopId, ShopBeauty shop) in beautyParser) {
            List<BeautyShopItemGroup> itemGroups = [];
            foreach (ShopBeauty.ItemGroup group in shop.itemGroup) {
                itemGroups.Add(new BeautyShopItemGroup(
                    StartTime: string.IsNullOrEmpty(group.saleStartTime) ? 0 : DateTime.ParseExact(group.saleStartTime, "yyyy-MM-dd-HH-mm-ss", CultureInfo.InvariantCulture).ToEpochSeconds(),
                    Items: ParseBeautyShopItems(group.item).ToArray()));
            }

            entries.Add(shopId, new BeautyShopMetadata(
                Id: shop.shopID,
                Category: (BeautyShopCategory) shop.categoryID,
                SubType: shop.shopID switch {
                    // Hardcoding this because I'm not sure where this information is located in the xmls
                    500 => 16,
                    501 => 19,
                    504 => 17,
                    505 => 28,
                    506 => 18,
                    508 => 21,
                    509 => 0,
                    510 => 20,
                    _ => 0,
                },
                StyleCostMetadata: new BeautyShopCostMetadata(
                    CurrencyType: (ShopCurrencyType) shop.stylePaymentType,
                    Price: shop.stylePrice,
                    Icon: shop.stylePaymentIconTag,
                    PaymentItemId: shop.stylePaymentItemID),
                ColorCostMetadata: new BeautyShopCostMetadata(
                    CurrencyType: (ShopCurrencyType) shop.colorPaymentType,
                    Price: shop.colorPrice,
                    Icon: shop.colorPaymentIconTag,
                    PaymentItemId: shop.colorPaymentItemID),
                IsRandom: shop.random,
                IsByItem: shop.byItem,
                ReturnCouponId: shop.returnCouponID,
                CouponId: shop.displayCouponID,
                CouponTag: Enum.TryParse(shop.couponTag, out ItemTag tag) ? tag : ItemTag.None,
                Items: ParseBeautyShopItems(shop.item).ToArray(),
                ItemGroups: itemGroups.ToArray()));
        }
        return entries;

        IEnumerable<BeautyShopItem> ParseBeautyShopItems(IList<ShopBeauty.Item> items) {
            foreach (ShopBeauty.Item item in items) {
                yield return new BeautyShopItem(
                    Id: item.id,
                    Cost: new BeautyShopCostMetadata(
                        CurrencyType: (ShopCurrencyType) item.paymentType,
                        Price: item.price,
                        Icon: item.paymentIconTag,
                        PaymentItemId: item.paymentItemID),
                    Weight: item.weight,
                    AchievementId: item.achieveID,
                    AchievementRank: (byte) item.achieveGrade,
                    RequiredLevel: item.requireLevel,
                    SaleTag: (ShopItemLabel) item.saleTag);
            }
        }
    }

    private MeretMarketTable ParseMeretCustomShop() {
        var results = new Dictionary<int, MeretMarketItemMetadata>();
        foreach ((int id, ShopMeretCustom entry) in parser.ParseShopMeretCustom()) {
            foreach (ShopMeretCustom addQuantity in entry.additionalQuantity) {
                results.Add(addQuantity.id, ParseMarketItemMetadata(addQuantity, entry));
            }
            results.Add(id, ParseMarketItemMetadata(entry));
        }
        return new MeretMarketTable(results);

        MeretMarketItemMetadata ParseMarketItemMetadata(ShopMeretCustom item, ShopMeretCustom? parent = null) {
            string saleStartTime = string.IsNullOrEmpty(parent?.saleStartTime) ? item.saleStartTime : parent.saleStartTime;
            string saleEndTime = string.IsNullOrEmpty(parent?.saleEndTime) ? item.saleEndTime : parent.saleEndTime;
            string promoStartTime = string.IsNullOrEmpty(parent?.promoSaleStartTime) ? item.promoSaleStartTime : parent.promoSaleStartTime;
            string promoEndTime = string.IsNullOrEmpty(parent?.promoSaleEndTime) ? item.promoSaleEndTime : parent.promoSaleEndTime;
            int[] jobRequirement = parent?.jobRequire ?? item.jobRequire;
            return new MeretMarketItemMetadata(
                Id: item.id,
                ParentId: parent?.id ?? 0,
                TabId: parent?.tabID ?? item.tabID,
                Banner: item.banner,
                BannerTag: (MeretMarketBannerTag) item.bannerTag,
                ItemId: parent?.itemID ?? item.itemID,
                Rarity: (byte) (parent?.grade ?? item.grade),
                Quantity: item.quantity,
                BonusQuantity: item.bonusQuantity,
                DurationInDays: item.durationDay,
                SaleTag: (MeretMarketItemSaleTag) item.saleTag,
                CurrencyType: (MeretMarketCurrencyType) (parent?.paymentType ?? item.paymentType),
                Price: item.price,
                SalePrice: item.salePrice == 0 ? item.price : item.salePrice,
                SaleStartTime: string.IsNullOrEmpty(saleStartTime) ? 0 : DateTime.ParseExact(saleStartTime, "yyyy-MM-dd-HH-mm-ss", CultureInfo.InvariantCulture).ToEpochSeconds(),
                SaleEndTime: string.IsNullOrEmpty(saleEndTime) ? 0 : DateTime.ParseExact(saleEndTime, "yyyy-MM-dd-HH-mm-ss", CultureInfo.InvariantCulture).ToEpochSeconds(),
                JobRequirement: jobRequirement.Select(job => (JobCode) job).AsEnumerable().FilterFlags(),
                RestockUnavailable: parent?.noRestock ?? item.noRestock,
                RequireMinLevel: parent?.minLevel ?? item.minLevel,
                RequireMaxLevel: parent?.maxLevel ?? item.maxLevel,
                RequireAchievementId: parent?.achieveID ?? item.achieveID,
                RequireAchievementRank: parent?.achieveGrade ?? item.achieveGrade,
                PcCafe: parent?.pcCafe ?? item.pcCafe,
                Giftable: parent?.giftable ?? item.giftable,
                ShowSaleTime: item.showSaleTime,
                PromoName: item.promoName,
                PromoStartTime: string.IsNullOrEmpty(promoStartTime) ? 0 : DateTime.ParseExact(promoStartTime, "yyyy-MM-dd-HH-mm-ss", CultureInfo.InvariantCulture).ToEpochSeconds(),
                PromoEndTime: string.IsNullOrEmpty(promoEndTime) ? 0 : DateTime.ParseExact(promoEndTime, "yyyy-MM-dd-HH-mm-ss", CultureInfo.InvariantCulture).ToEpochSeconds());
        }
    }

    // fishLure.xml: one row per lure buff and level (90000309 has levels 1-3), so the rows are keyed by both. A
    // catchProp without a catchRank counts for every rank (rank 0). Rows of a disabled feature or another locale
    // are left out.
    // FishLureRoot.lure resolves the rows per additionalEffectCode alone, so of a lure's levels it kept one (90000309,
    // the prize fish lure, has levels 1-3 and only level 3 was left). The rows are resolved per code and level.
    public static IEnumerable<FishLure> LureRows(FishLureRoot root) {
        return root.__lure.ResolveFeatureLocale(lure => (lure.additionalEffectCode, lure.additionalEffectLevel));
    }

    private FishLureRoot ParseFishLureRoot() {
        var serializer = new XmlSerializer(typeof(FishLureRoot));
        string xml = Sanitizer.RemoveEmpty(xmlReader.GetString(xmlReader.GetEntry("table/Server/fishLure.xml")));
        using XmlReader reader = XmlReader.Create(new StringReader(xml));
        return (FishLureRoot) serializer.Deserialize(reader)!;
    }

    public static Dictionary<int, IReadOnlyDictionary<short, FishTable.Lure>> MapLures(IEnumerable<FishLure> rows) {
        var lures = new Dictionary<int, Dictionary<short, FishTable.Lure>>();
        foreach (FishLure lure in rows) {
            if (!FeatureLocaleFilter.FeatureEnabled(lure.Feature) || !FeatureLocaleFilter.HasLocale(lure.Locale)) {
                continue;
            }

            short level = (short) lure.additionalEffectLevel;
            if (!lures.TryGetValue(lure.additionalEffectCode, out Dictionary<short, FishTable.Lure>? levels)) {
                levels = new Dictionary<short, FishTable.Lure>();
                lures.Add(lure.additionalEffectCode, levels);
            }
            levels[level] = new FishTable.Lure(
                BuffId: lure.additionalEffectCode,
                BuffLevel: level,
                Catches: RankRates(lure.catchRank, lure.catchProp),
                Spawns: RankRates(lure.spawnRank, lure.spawnProp),
                SpawnFishes: lure.spawnFish.Zip(lure.spawnFishRate, (fishId, rate) => new FishTable.Lure.Spawn(fishId, rate)).ToArray(),
                FishSizeClass: lure.fishSize,
                FishSizeProbability: lure.fishSizeProp,
                GlobalDrop: Drop(lure.globalDropBoxID, lure.globalDropRank, lure.globalDropFishingSpotMastery),
                IndividualDrop: Drop(lure.individualDropBoxID, lure.individualDropRank, lure.individualDropFishingSpotMastery));
        }
        return lures.ToDictionary(entry => entry.Key, entry => (IReadOnlyDictionary<short, FishTable.Lure>) entry.Value);

        static FishTable.Lure.RankRate[] RankRates(int[] ranks, int[] rates) {
            if (ranks.Length == 0) {
                return rates.Select(rate => new FishTable.Lure.RankRate(0, rate)).ToArray();
            }
            return ranks.Zip(rates, (rank, rate) => new FishTable.Lure.RankRate(rank, rate)).ToArray();
        }

        static FishTable.Lure.Drop? Drop(int boxId, int rank, int[] mastery) {
            if (boxId == 0) {
                return null;
            }
            FishTable.Range<int>? range = mastery.Length >= 2 ? new FishTable.Range<int>(mastery[0], mastery[1]) : null;
            return new FishTable.Lure.Drop(boxId, rank, range);
        }
    }

    private FishTable ParseFish() {
        // Parse Fish
        var fishes = new Dictionary<int, FishTable.Fish>();
        foreach ((int id, Fish fish) in parser.ParseFish()) {
            if (!Enum.TryParse(fish.habitat, out LiquidType liquidType)) {
                liquidType = LiquidType.all;
            }

            int[] smallSize = fish.smallSize.Split("-").Select(int.Parse).ToArray();
            int[] bigSize = fish.bigSize.Split("-").Select(int.Parse).ToArray();
            fishes.Add(id, new FishTable.Fish(
                Id: id,
                FluidHabitat: liquidType,
                Mastery: fish.fishMastery,
                Level: fish.lv,
                Rarity: fish.rank,
                PointCount: fish.pointCount,
                MasteryExp: fish.masteryPoint,
                Exp: fish.exp,
                FishingTime: fish.fishingTime,
                CatchProbability: fish.catchProp,
                BaitProbability: fish.baitProp,
                SmallSize: new FishTable.Range<int>(smallSize[0], smallSize[1]),
                BigSize: new FishTable.Range<int>(bigSize[0], bigSize[1]),
                BaitEffectIds: fish.bait,
                IndividualDropBoxId: fish.individualDropBoxID,
                IgnoreSpotMastery: fish.ignoreSpotMastery));
        }

        // Parse Spots
        var spots = new Dictionary<int, FishTable.Spot>();
        foreach ((int mapId, FishingSpot spot) in parser.ParseFishingSpot()) {
            var liquidTypes = new List<LiquidType>();
            foreach (string liquidType in spot.liquidType) {
                if (Enum.TryParse(liquidType, out LiquidType type)) {
                    liquidTypes.Add(type);
                }
            }

            spots.Add(mapId, new FishTable.Spot(
                Id: mapId,
                MinMastery: spot.minMastery,
                MaxMastery: spot.maxMastery,
                LiquidTypes: liquidTypes,
                GlobalFishBoxId: spot.globalFishBoxID,
                IndividualFishBoxId: spot.individualFishBoxID,
                GlobalDropBoxId: spot.globalDropBoxId,
                IndividualDropBoxId: spot.individualDropBoxId,
                SpotLevel: spot.spotLevel,
                DropRank: spot.spotDropRank ? 1 : 0)); // TODO: Change this from a bool to int
        }

        Dictionary<int, IReadOnlyDictionary<short, FishTable.Lure>> lures = MapLures(LureRows(ParseFishLureRoot()));

        // Global Fish Boxes
        Dictionary<int, FishTable.FishBox> globalBoxes = ParseFishBox(parser.ParseGlobalFishBox());
        Dictionary<int, FishTable.FishBox> individualBoxes = ParseFishBox(parser.ParseIndividualFishBox());

        return new FishTable(fishes, spots, lures, globalBoxes, individualBoxes);

        Dictionary<int, FishTable.FishBox> ParseFishBox(IEnumerable<(int, FishBox)> boxes) {
            var results = new Dictionary<int, FishTable.FishBox>();
            foreach ((int id, FishBox box) in boxes) {
                Dictionary<int, int> fishes = [];
                foreach (FishBox.Fish data in box.fish) {
                    fishes[data.fishCode] = data.weight;
                }

                results.Add(id, new FishTable.FishBox(
                    Id: id,
                    Probability: box.probability,
                    CubeRate: box.cubeRate,
                    Fishes: fishes));
            }
            return results;
        }
    }

    private CombineSpawnTable ParseCombineSpawn() {
        var groupDict = new Dictionary<int, Dictionary<int, SpawnGroupMetadata>>();
        var npcDict = new Dictionary<int, Dictionary<int, SpawnNpcMetadata>>();
        var objectDict = new Dictionary<int, Dictionary<int, SpawnInteractObjectMetadata>>();

        foreach ((int id, SpawnGroup spawnGroup) in parser.ParseSpawnGroup()) {
            var groupMetadata = new SpawnGroupMetadata(
                GroupId: spawnGroup.groupId,
                Type: Enum.TryParse(spawnGroup.groupType, out CombineSpawnGroupType type) ? type : CombineSpawnGroupType.none,
                TotalCount: spawnGroup.combineCount,
                ResetTick: spawnGroup.resetTick,
                MapId: spawnGroup.fieldId);

            if (!groupDict.TryGetValue(spawnGroup.fieldId, out Dictionary<int, SpawnGroupMetadata>? value)) {
                value = new Dictionary<int, SpawnGroupMetadata>();
                groupDict[spawnGroup.fieldId] = value;
            }

            value[spawnGroup.groupId] = groupMetadata;
        }

        foreach ((int id, SpawnNpc spawn) in parser.ParseSpawnNpc()) {
            var spawnMetadata = new SpawnNpcMetadata(
                CombineId: spawn.combineId,
                GroupId: spawn.groupId,
                Weight: spawn.weight,
                SpawnId: spawn.spawnId);

            if (!npcDict.TryGetValue(spawn.groupId, out Dictionary<int, SpawnNpcMetadata>? value)) {
                value = new Dictionary<int, SpawnNpcMetadata>();
                npcDict[spawn.groupId] = value;
            }

            value[spawn.combineId] = spawnMetadata;
        }

        foreach ((int id, SpawnInteractObject interactObject) in parser.ParseSpawnInteractObject()) {
            var objectMetadata = new SpawnInteractObjectMetadata(
                CombineId: interactObject.combineId,
                GroupId: interactObject.groupId,
                Weight: interactObject.weight,
                RegionSpawnId: interactObject.regionSpawnId,
                InteractId: interactObject.interactId,
                Model: interactObject.model,
                Asset: interactObject.asset,
                Normal: interactObject.normal,
                Reactable: interactObject.reactable,
                Scale: interactObject.scale,
                KeepAnimate: interactObject.isKeepAnimate);

            if (!objectDict.TryGetValue(interactObject.groupId, out Dictionary<int, SpawnInteractObjectMetadata>? value)) {
                value = new Dictionary<int, SpawnInteractObjectMetadata>();
                objectDict[interactObject.groupId] = value;
            }

            value[interactObject.combineId] = objectMetadata;
        }

        return new CombineSpawnTable(groupDict, npcDict, objectDict);
    }

    private EnchantOptionTable ParseEnchantOption() {
        var results = new Dictionary<int, EnchantOptionMetadata>();
        foreach ((int id, EnchantOption enchantOption) in parser.ParseEnchantOption()) {
            IList<BasicAttribute> basicAttributes = [];
            foreach (int option in enchantOption.option) {
                if (!Enum.TryParse(option.ToString(), out BasicAttribute basicAttribute)) {
                    Console.WriteLine($"Failed to parse basic attribute {option}");
                    continue;
                }
                basicAttributes.Add(basicAttribute);
            }
            results.Add(id, new EnchantOptionMetadata(
                Id: id,
                Slot: enchantOption.slot,
                EnchantLevel: enchantOption.grade,
                Rarity: (short) enchantOption.rank,
                Rate: enchantOption.rate,
                MinLevel: enchantOption.minLv,
                MaxLevel: enchantOption.maxLv,
                Attributes: basicAttributes.ToArray()));
        }
        return new EnchantOptionTable(results);
    }

    private ScriptEventConditionTable ParseScriptEventConditionTable() {
        var results = new Dictionary<ScriptEventType, Dictionary<int, ScriptEventConditionMetadata>>();

        foreach ((int eventId, ScriptEventCondition scriptEventCondition) in parser.ParseScriptEventCondition()) {
            ScriptEventType type = scriptEventCondition.type switch {
                Parser.Enum.ScriptEventType.enchant_fail => ScriptEventType.EnchantFail,
                Parser.Enum.ScriptEventType.enchant_item_select => ScriptEventType.EnchantSelect,
                Parser.Enum.ScriptEventType.enchant_complete => ScriptEventType.EnchantComplete,
                Parser.Enum.ScriptEventType.merge_select => ScriptEventType.EmpowerSelect,
                Parser.Enum.ScriptEventType.merge_try => ScriptEventType.EmpowerTry,
                Parser.Enum.ScriptEventType.merge_result => ScriptEventType.EmpowerResult,
                Parser.Enum.ScriptEventType.remake_fail => ScriptEventType.RerollFail,
                Parser.Enum.ScriptEventType.remake_item_select => ScriptEventType.RerollItemSelect,
                Parser.Enum.ScriptEventType.remake_option_select => ScriptEventType.RerollOptionSelect,
                Parser.Enum.ScriptEventType.remake_complete => ScriptEventType.RerollComplete,
                _ => ScriptEventType.EnchantFail,
            };

            List<int> enchantLevels = [];
            if (scriptEventCondition.enchantLevel.Contains('-')) {
                string[] enchantLevelSplit = scriptEventCondition.enchantLevel.Split('-');
                int startInt = int.TryParse(enchantLevelSplit[0], out int start) ? start : 0;
                int endInt = int.TryParse(enchantLevelSplit[1], out int end) ? end : 0;

                for (int i = startInt; i <= endInt; i++) {
                    enchantLevels.Add(i);
                }
            } else {
                if (!int.TryParse(scriptEventCondition.enchantLevel, out int enchantLevel)) {
                    if (scriptEventCondition.enchantLevel == "MAX") {
                        enchantLevels.Add(15);
                    }
                } else {
                    enchantLevels.Add(enchantLevel);
                }
            }

            if (!results.TryGetValue(type, out Dictionary<int, ScriptEventConditionMetadata>? value)) {
                value = new Dictionary<int, ScriptEventConditionMetadata>();
                results[type] = value;
            }

            value[scriptEventCondition.id] = new ScriptEventConditionMetadata(
                Id: scriptEventCondition.id,
                EventType: type,
                ErrorCode: (ItemEnchantError) scriptEventCondition.enchantError,
                Rarity: (short) scriptEventCondition.rank,
                EnchantLevel: enchantLevels.ToArray(),
                FailCount: scriptEventCondition.failCount,
                DamageType: (EnchantDamageType) scriptEventCondition.isDamaged,
                ResultType: (EnchantResult) scriptEventCondition.result
            );
        }
        return new ScriptEventConditionTable(results);
    }

    private UnlimitedEnchantOptionTable ParseUnlimitedEnchantOption() {
        var results = new Dictionary<int, Dictionary<int, UnlimitedEnchantOptionTable.Option>>();
        foreach ((int slot, IDictionary<int[], UnlimitedEnchantOption> enchantOptions) in parser.ParseUnlimitedEnchantOption()) {
            var levelDictionary = new Dictionary<int, UnlimitedEnchantOptionTable.Option>();
            foreach ((int[] optionLevel, UnlimitedEnchantOption enchantOption) in enchantOptions) {
                int minLevel = optionLevel[0];
                int maxLevel = optionLevel.Length > 1 ? optionLevel[1] : minLevel;

                for (int level = minLevel; level <= maxLevel; level++) {
                    Dictionary<BasicAttribute, int> values = [];
                    Dictionary<BasicAttribute, float> rates = [];
                    Dictionary<SpecialAttribute, int> specialValues = [];
                    Dictionary<SpecialAttribute, float> specialRates = [];
                    AddBasic(values, rates, (BasicAttribute) enchantOption.option1, enchantOption.value1, enchantOption.rate1);
                    AddBasic(values, rates, (BasicAttribute) enchantOption.option2, enchantOption.value2, enchantOption.rate2);
                    AddBasic(values, rates, (BasicAttribute) enchantOption.option3, enchantOption.value3, enchantOption.rate3);
                    AddBasic(values, rates, (BasicAttribute) enchantOption.option4, enchantOption.value4, enchantOption.rate4);
                    AddSpecial(specialValues, specialRates, (SpecialAttribute) enchantOption.sa_option1, enchantOption.sa_value1, enchantOption.sa_rate1);
                    AddSpecial(specialValues, specialRates, (SpecialAttribute) enchantOption.sa_option2, enchantOption.sa_value2, enchantOption.sa_rate2);

                    levelDictionary[level] = new UnlimitedEnchantOptionTable.Option(
                        Values: values,
                        Rates: rates,
                        SpecialValues: specialValues,
                        SpecialRates: specialRates);

                }
            }
            results[slot] = levelDictionary;
        }
        return new UnlimitedEnchantOptionTable(results);

        void AddBasic(Dictionary<BasicAttribute, int> values, Dictionary<BasicAttribute, float> rates, BasicAttribute attribute, int value, float rate) {
            if (value != 0) {
                values.Add(attribute, value);
            }
            if (rate != 0) {
                rates.Add(attribute, rate);
            }
        }

        void AddSpecial(Dictionary<SpecialAttribute, int> values, Dictionary<SpecialAttribute, float> rates, SpecialAttribute attribute, int value, float rate) {
            if (attribute == SpecialAttribute.None) {
                return;
            }
            if (value != 0) {
                values.Add(attribute, value);
            }
            if (rate != 0) {
                rates.Add(attribute, rate);
            }
        }
    }

    // A pack without the table gives an empty one: no createRandomRoom portal opens.
    private RoomRandomTable ParseRoomRandom() {
        if (xmlReader.GetEntry("table/Server/room_random.xml") == null) {
            return MapRoomRandom([], []);
        }
        return MapRoomRandom(parser.ParseRoomRandom().Select(entry => entry.RandmRoom).ToList(),
            parser.ParseRoom().Select(entry => entry.Room).ToList());
    }

    public static RoomRandomTable MapRoomRandom(IEnumerable<RandomRoom> randomRooms, IEnumerable<RoomRandom> rooms) {
        var randomRoomTable = new Dictionary<int, RandomRoomMetadata>();
        foreach (RandomRoom randomRoom in randomRooms) {
            randomRoomTable[randomRoom.id] = new RandomRoomMetadata(
                Id: randomRoom.id,
                Prob: randomRoom.prob,
                Refs: (randomRoom.@ref ?? []).Select(entry => new RandomRoomMetadata.Ref(entry.roomid, entry.weight)).ToArray());
        }

        var roomTable = new Dictionary<int, RandomRoomFieldMetadata>();
        foreach (RoomRandom room in rooms) {
            roomTable[room.id] = new RandomRoomFieldMetadata(
                Id: room.id,
                FieldIds: room.fieldIDs ?? [],
                DurationTick: room.roomdurationTick,
                AssetName: room.nifAssetName ?? string.Empty,
                MaxUserCount: room.maxUserCount,
                AutoClosing: room.autoClosing != 0);
        }

        return new RoomRandomTable(randomRoomTable, roomTable);
    }

    private NpcStatFactorTable ParseNpcStatFactor() {
        Maple2.File.IO.Crypto.Common.PackFileEntry? entry = xmlReader.Files.FirstOrDefault(file =>
            file.Name.Equals("table/Server/npcStatFactorByPlayerCount.xml", StringComparison.OrdinalIgnoreCase));
        if (entry == null) {
            return new NpcStatFactorTable(new Dictionary<int, IReadOnlyDictionary<int, IReadOnlyDictionary<int, NpcStatFactorMetadata>>>());
        }
        NpcStatFactorTable table = MapNpcStatFactor(xmlReader.GetXmlDocument(entry),
            row => FeatureLocaleFilter.FeatureEnabled(row.GetAttribute("feature")) && FeatureLocaleFilter.HasLocale(row.GetAttribute("locale")));
        Console.WriteLine($"npcStatFactorByPlayerCount: {table.Entries.Values.Sum(classes => classes.Values.Sum(counts => counts.Count))} rows after the feature filter");
        return table;
    }

    // The locale's file, else the NA one (the parser reads the server tables of every locale from NA). KR and NA
    // ship the same 12 seasons, CN 7.
    private WorldChampionTable ParseWorldChampion() {
        string[] paths = [$"table/Server/{FeatureLocaleFilter.Locale}/WorldChampionBase.xml", "table/Server/NA/WorldChampionBase.xml"];
        foreach (string path in paths) {
            XmlDocument? document = null;
            string? text = overlay.Read(path);
            if (text != null) {
                document = new XmlDocument();
                document.LoadXml(text);
                Console.WriteLine($"WorldChampionBase: {path} from {ServerOverlay.Option}");
            } else {
                Maple2.File.IO.Crypto.Common.PackFileEntry? entry = xmlReader.Files.FirstOrDefault(file =>
                    file.Name.Equals(path, StringComparison.OrdinalIgnoreCase));
                if (entry != null) {
                    document = xmlReader.GetXmlDocument(entry);
                }
            }
            if (document == null) {
                continue;
            }
            WorldChampionTable table = MapWorldChampion(document, row => FeatureLocaleFilter.FeatureEnabled(row.GetAttribute("feature")));
            Console.WriteLine($"WorldChampionBase: {table.Entries.Count} seasons from {path}");
            return table;
        }
        return new WorldChampionTable(new Dictionary<int, WorldChampionTable.Season>());
    }

    // A season without a readable begin or end, or that ends before it begins, or without steps, is left out (with a
    // line); the steps are in StepID order.
    public static WorldChampionTable MapWorldChampion(XmlDocument document, Func<XmlElement, bool> available) {
        var seasons = new Dictionary<int, WorldChampionTable.Season>();
        foreach (XmlElement row in document.SelectNodes("//WorldChampion")?.OfType<XmlElement>() ?? []) {
            if (!available(row)) {
                continue;
            }
            int id = IntAttribute(row, "seasonID");
            DateTime begin = AttendGiftReplay.ParseTime(row.GetAttribute("activeBegin"));
            DateTime end = AttendGiftReplay.ParseTime(row.GetAttribute("activeEnd"));
            var steps = row.SelectNodes("Step")?.OfType<XmlElement>()
                .Select(step => new WorldChampionTable.Step(IntAttribute(step, "StepID"), IntAttribute(step, "npcID"), IntAttribute(step, "rewardID")))
                .Where(step => step.Id > 0 && step.NpcId > 0)
                .OrderBy(step => step.Id)
                .ToList() ?? [];
            if (id <= 0 || begin == DateTime.MinValue || end == DateTime.MinValue || end < begin || steps.Count == 0) {
                Console.WriteLine($"WorldChampionBase: season {row.GetAttribute("seasonID")} left out (begin \"{row.GetAttribute("activeBegin")}\", end \"{row.GetAttribute("activeEnd")}\", {steps.Count} steps)");
                continue;
            }
            long.TryParse(row.GetAttribute("rewardDamage"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long rewardDamage);
            seasons[id] = new WorldChampionTable.Season(id, IntAttribute(row, "fieldID"), IntAttribute(row, "spawnPointID"), begin, end,
                rewardDamage, IntAttribute(row, "systemMailID"), steps);
        }
        return new WorldChampionTable(seasons);

        static int IntAttribute(XmlElement element, string name) {
            return int.TryParse(element.GetAttribute(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 0;
        }
    }

    private ArcadeRewardTable ParseArcadeReward() {
        Maple2.File.IO.Crypto.Common.PackFileEntry? entry = xmlReader.Files.FirstOrDefault(file =>
            file.Name.Equals("table/Server/arcadeReward.xml", StringComparison.OrdinalIgnoreCase));
        ArcadeRewardTable table = entry == null ? new ArcadeRewardTable(new Dictionary<int, IReadOnlyDictionary<int, ArcadeRewardTable.Entry>>())
            : MapArcadeReward(xmlReader.GetXmlDocument(entry), row => FeatureLocaleFilter.FeatureEnabled(row.GetAttribute("feature")) && FeatureLocaleFilter.HasLocale(row.GetAttribute("locale")));
        Console.WriteLine($"arcadeReward: {table.Entries.Values.Sum(rounds => rounds.Count)} rounds of {table.Entries.Count} arcades");
        return table;
    }

    // A row of a locale replaces the row without a locale of the same round, in either order; rows the filter drops
    // are left out. A second arcadeGame node with the same arcadeId adds its rows to the first one's (the shipped
    // file has one node per arcade).
    public static ArcadeRewardTable MapArcadeReward(XmlDocument document, Func<XmlElement, bool> available) {
        var games = new Dictionary<int, (Dictionary<int, ArcadeRewardTable.Entry> Rounds, HashSet<int> Localized)>();
        foreach (XmlElement game in document.SelectNodes("ms2/arcadeGame")!.OfType<XmlElement>()) {
            if (!int.TryParse(game.GetAttribute("arcadeId"), out int arcadeId)) continue;
            if (!games.TryGetValue(arcadeId, out var known)) {
                known = (new Dictionary<int, ArcadeRewardTable.Entry>(), new HashSet<int>());
                games[arcadeId] = known;
            }
            (Dictionary<int, ArcadeRewardTable.Entry> rounds, HashSet<int> localized) = known;
            foreach (XmlElement row in game.SelectNodes("reward")!.OfType<XmlElement>()) {
                if (!available(row) || !int.TryParse(row.GetAttribute("round"), out int round)) continue;
                bool hasLocale = row.GetAttribute("locale") != "";
                if (!hasLocale && localized.Contains(round)) continue;
                if (hasLocale) localized.Add(round);
                float.TryParse(row.GetAttribute("expRate"), NumberStyles.Float, CultureInfo.InvariantCulture, out float expRate);
                int.TryParse(row.GetAttribute("meso"), out int meso);
                rounds[round] = new ArcadeRewardTable.Entry(round, expRate, meso);
            }
        }
        return new ArcadeRewardTable(games.ToDictionary(game => game.Key,
            game => (IReadOnlyDictionary<int, ArcadeRewardTable.Entry>) game.Value.Rounds));
    }

    private TermTagTable ParseTermTag() {
        Maple2.File.IO.Crypto.Common.PackFileEntry? entry = xmlReader.Files.FirstOrDefault(file =>
            file.Name.Equals("table/Server/TermTag.xml", StringComparison.OrdinalIgnoreCase));
        if (entry == null) {
            return new TermTagTable(new Dictionary<int, TermTagTable.Entry>());
        }
        TermTagTable table = MapTermTag(xmlReader.GetXmlDocument(entry), ClientListedRows.Available);
        Console.WriteLine($"TermTag: {table.Entries.Count} terms");
        return table;
    }

    // KST: the KMS2 term load (0x137) sends startDate "2022-03-17-08-00" as 1647471600, 08:00 at UTC+9.
    private static readonly TimeSpan TermTagOffset = TimeSpan.FromHours(9);

    // <term id name [feature] [locale] [tagComment]><v startDate endDate/></term>, dates "yyyy-MM-dd-HH-mm[-ss]".
    // Rows the filter drops, rows without a period and rows whose dates do not read are left out.
    public static TermTagTable MapTermTag(XmlDocument document, Func<XmlElement, bool> available) {
        var entries = new Dictionary<int, TermTagTable.Entry>();
        foreach (XmlElement row in document.SelectNodes("ms2/term")!.OfType<XmlElement>()) {
            if (!available(row) || !int.TryParse(row.GetAttribute("id"), out int id)) continue;
            if (row.SelectSingleNode("v") is not XmlElement period) continue;
            long? start = TermTagTime(period.GetAttribute("startDate"));
            long? end = TermTagTime(period.GetAttribute("endDate"));
            if (start == null || end == null) continue;
            entries[id] = new TermTagTable.Entry(id, row.GetAttribute("name"), start.Value, end.Value);
        }
        return new TermTagTable(entries);
    }

    private static long? TermTagTime(string value) {
        string[] parts = value.Split('-');
        if (parts.Length is not (5 or 6)) return null;
        var fields = new int[6];
        for (int i = 0; i < parts.Length; i++) {
            if (!int.TryParse(parts[i], out fields[i])) return null;
        }
        try {
            return new DateTimeOffset(fields[0], fields[1], fields[2], fields[3], fields[4], fields[5], TermTagOffset).ToUnixTimeSeconds();
        } catch (ArgumentOutOfRangeException) {
            return null;
        }
    }

    private HideAndSeekTable ParseHideAndSeek() {
        Maple2.File.IO.Crypto.Common.PackFileEntry? entry = xmlReader.Files.FirstOrDefault(file =>
            file.Name.Equals("table/Server/hideAndSeek.xml", StringComparison.OrdinalIgnoreCase));
        if (entry == null || clientReader == null) {
            return new HideAndSeekTable(new Dictionary<int, HideAndSeekTable.Field>());
        }
        var nifs = new Dictionary<int, string>();
        foreach ((int id, Maple2.File.Parser.Xml.Riding.Riding riding) in new RidingParser(clientReader).Parse()) {
            nifs[id] = riding.basic.nif;
        }
        HideAndSeekTable table = MapHideAndSeek(xmlReader.GetXmlDocument(entry),
            row => FeatureLocaleFilter.FeatureEnabled(row.GetAttribute("feature")) && FeatureLocaleFilter.HasLocale(row.GetAttribute("locale")),
            rideId => nifs.GetValueOrDefault(rideId), HideAndSeekProps);
        Console.WriteLine($"hideAndSeek: {table.Entries.Count} fields, {table.Entries.Values.Sum(field => field.Props.Count)} rides with a prop");
        return table;
    }

    // The map's xblock (map/<id>.xml environment xblock name, the first one) -> model name (lower case, the trailing
    // "_" of xblock model names dropped) -> ObjectSearchIndex of its props.
    private Dictionary<string, List<int>> HideAndSeekProps(int mapId) {
        var props = new Dictionary<string, List<int>>();
        Maple2.File.IO.Crypto.Common.PackFileEntry? mapEntry = clientReader?.Files.FirstOrDefault(file =>
            file.Name.Equals($"map/{mapId:D8}.xml", StringComparison.OrdinalIgnoreCase));
        if (mapEntry == null || exportedReader == null) return props;
        string? xblock = (clientReader!.GetXmlDocument(mapEntry).SelectSingleNode("ms2/environment/xblock") as XmlElement)?.GetAttribute("name");
        if (string.IsNullOrEmpty(xblock)) return props;
        Maple2.File.IO.Crypto.Common.PackFileEntry? xblockEntry = exportedReader.Files.FirstOrDefault(file =>
            file.Name.Equals($"xblock/{xblock}.xblock", StringComparison.OrdinalIgnoreCase));
        if (xblockEntry == null) return props;
        return MapXBlockSearchIndex(exportedReader.GetXmlDocument(xblockEntry));
    }

    public static Dictionary<string, List<int>> MapXBlockSearchIndex(XmlDocument xblock) {
        var props = new Dictionary<string, List<int>>();
        foreach (XmlElement entity in xblock.SelectNodes("game/entitySet/entity")!.OfType<XmlElement>()) {
            var index = entity.SelectSingleNode("property[@name='ObjectSearchIndex']/set") as XmlElement;
            if (index == null || !int.TryParse(index.GetAttribute("value"), out int value)) continue;
            string model = entity.GetAttribute("modelName").TrimEnd('_').ToLowerInvariant();
            if (!props.TryGetValue(model, out List<int>? list)) {
                props[model] = list = [];
            }
            list.Add(value);
        }
        return props;
    }

    // A row of a locale replaces the row without a locale of the same map, in either order; rows the filter drops are
    // left out. Props lists, per ride of the field, the field's prop indexes of the ride's nif (rides without one are
    // left out of Props).
    public static HideAndSeekTable MapHideAndSeek(XmlDocument document, Func<XmlElement, bool> available, Func<int, string?> nifOf,
                                                  Func<int, Dictionary<string, List<int>>> propsOf) {
        var rows = new Dictionary<int, (List<int> Rides, bool Localized)>();
        foreach (XmlElement row in document.SelectNodes("ms2/field")!.OfType<XmlElement>()) {
            if (!available(row) || !int.TryParse(row.GetAttribute("mapCode"), out int mapId)) continue;
            bool hasLocale = row.GetAttribute("locale") != "";
            if (!hasLocale && rows.TryGetValue(mapId, out var known) && known.Localized) continue;
            List<int> rides = row.GetAttribute("ridingObject").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(id => int.TryParse(id, out int rideId) ? rideId : 0).Where(id => id != 0).ToList();
            rows[mapId] = (rides, hasLocale);
        }
        var entries = new Dictionary<int, HideAndSeekTable.Field>();
        foreach ((int mapId, (List<int> rides, _)) in rows) {
            Dictionary<string, List<int>> props = propsOf(mapId);
            var rideProps = new Dictionary<int, IReadOnlyList<int>>();
            foreach (int rideId in rides) {
                string? nif = nifOf(rideId)?.ToLowerInvariant();
                if (nif != null && props.TryGetValue(nif, out List<int>? indexes)) {
                    rideProps[rideId] = indexes;
                }
            }
            entries[mapId] = new HideAndSeekTable.Field(rides, rideProps);
        }
        return new HideAndSeekTable(entries);
    }

    private MonsterExpTable ParseMonsterExp() {
        Maple2.File.IO.Crypto.Common.PackFileEntry? entry = xmlReader.Files.FirstOrDefault(file =>
            file.Name.Equals("table/Server/monsterExpTable.xml", StringComparison.OrdinalIgnoreCase));
        MonsterExpTable table = entry == null ? new MonsterExpTable(new Dictionary<int, MonsterExpMetadata>())
            : MapMonsterExp(xmlReader.GetXmlDocument(entry));
        Console.WriteLine($"monsterExpTable: {table.Entries.Count} levels");
        return table;
    }

    // A row without a level is skipped, an empty column is 0; a later row for the same level replaces an earlier one.
    public static MonsterExpTable MapMonsterExp(XmlDocument document) {
        var results = new Dictionary<int, MonsterExpMetadata>();
        foreach (XmlNode node in document.SelectNodes("ms2/exp")!) {
            if (node is not XmlElement row || !int.TryParse(row.GetAttribute("level"), out int level)) continue;
            long Column(string name) => long.TryParse(row.GetAttribute(name), out long value) ? value : 0;
            results[level] = new MonsterExpMetadata(
                Common: Column("common"),
                Elite: Column("elite"),
                Boss: Column("boss"),
                DungeonElite: Column("dungeonElite"),
                DungeonBoss: Column("dungeonBoss"));
        }
        return new MonsterExpTable(results);
    }

    // Rows with an empty factorID, class or playerCount (the file ends with a few) are skipped; keep says whether a
    // row's feature and locale are on. A later row for the same (factorID, class, playerCount) replaces an earlier one.
    public static NpcStatFactorTable MapNpcStatFactor(XmlDocument document, Func<XmlElement, bool> keep) {
        var results = new Dictionary<int, Dictionary<int, Dictionary<int, NpcStatFactorMetadata>>>();
        foreach (XmlNode node in document.SelectNodes("ms2/PlayerCountFactor")!) {
            if (node is not XmlElement row || !keep(row)) continue;
            if (!int.TryParse(row.GetAttribute("factorID"), out int factorId)
                || !int.TryParse(row.GetAttribute("class"), out int npcClass)
                || !int.TryParse(row.GetAttribute("playerCount"), out int playerCount)) {
                continue;
            }

            double Read(string name, double fallback) {
                return double.TryParse(row.GetAttribute(name), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : fallback;
            }

            if (!results.TryGetValue(factorId, out Dictionary<int, Dictionary<int, NpcStatFactorMetadata>>? classes)) {
                results[factorId] = classes = new Dictionary<int, Dictionary<int, NpcStatFactorMetadata>>();
            }
            if (!classes.TryGetValue(npcClass, out Dictionary<int, NpcStatFactorMetadata>? counts)) {
                classes[npcClass] = counts = new Dictionary<int, NpcStatFactorMetadata>();
            }
            counts[playerCount] = new NpcStatFactorMetadata(
                HpRate: Read("hpRate", 1), HpValue: Read("hpValue", 0),
                PapRate: Read("papRate", 1), PapValue: Read("papValue", 0),
                MapRate: Read("mapRate", 1), MapValue: Read("mapValue", 0),
                NddRate: Read("nddRate", 1), NddValue: Read("nddValue", 0),
                CapRate: Read("capRate", 1), CapValue: Read("capValue", 0));
        }

        return new NpcStatFactorTable(results.ToDictionary(
            factor => factor.Key,
            factor => (IReadOnlyDictionary<int, IReadOnlyDictionary<int, NpcStatFactorMetadata>>) factor.Value.ToDictionary(
                npcClass => npcClass.Key,
                npcClass => (IReadOnlyDictionary<int, NpcStatFactorMetadata>) npcClass.Value)));
    }

    private ConstantsTable ParseConstants() {
        var constants = new ConstantsTable();
        Dictionary<string, PropertyInfo> propertyLookup = typeof(ConstantsTable).GetProperties()
            .ToDictionary(p => p.Name.Trim(), p => p, StringComparer.OrdinalIgnoreCase);
        var serverRows = new List<(string, string)>();
        foreach ((string key, Constants.Key constant) in parser.ParseConstants()) {
            string trimmedKey = key.Trim();
            serverRows.Add((trimmedKey, constant.value));
            if (!propertyLookup.TryGetValue(trimmedKey, out PropertyInfo? property)) continue;
            string cleanValue = CleanInput(constant.value.Trim(), trimmedKey, property.PropertyType);
            GenericHelper.SetValue(property, constants, cleanValue);
        }

        var rowLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach ((string key, string value) in serverRows) {
            rowLookup[key] = value; // a key that is there twice: the last row, as for the single fields above
        }
        constants = constants with {
            WarpOpenContinents = ConstantsTable.ReadList(rowLookup, "WarpOpenContinent"),
            EquipmentSellPriceFactors = ConstantsTable.ReadMultiControl(MultiControlRows(), "equipmentSellPrice"),
            QuestRewardMesoFactors = ConstantsTable.ReadMultiControl(MultiControlRows(), "questRewardMeso"),
        };

        // Prices: the client's own row for this locale wins, so the server takes what the client's box shows.
        foreach (ClientPriceConstants.Override price in ClientPriceConstants.Differences(ClientPriceConstants.Select(serverRows), ClientPriceRows())) {
            if (!propertyLookup.TryGetValue(price.Key, out PropertyInfo? property)) continue;
            Console.WriteLine($"constants: {property.Name} = {price.ClientValue} from the client table (server pack: {price.ServerValue ?? "no row"})");
            GenericHelper.SetValue(property, constants, CleanInput(price.ClientValue, property.Name, property.PropertyType));
        }
        return constants;

        // The multicontrol table of LOCALE's own folder: the played client's pack, else the data folder's client
        // pack, else the server pack. A pack of another region has no such file, so its factors do not apply.
        List<(string, string, string)> MultiControlRows() {
            var rows = new List<(string, string, string)>();
            string name = $"table/{FeatureLocaleFilter.Locale.ToLowerInvariant()}/multicontrol.xml";
            foreach (M2dReader? reader in new[] { playedReader, clientReader, xmlReader }) {
                Maple2.File.IO.Crypto.Common.PackFileEntry? entry = reader?.Files.FirstOrDefault(file => file.Name == name);
                if (reader == null || entry == null) {
                    continue;
                }
                foreach (XmlNode node in reader.GetXmlDocument(entry).SelectNodes("ms2/multi")!) {
                    if (node is not XmlElement row) continue;
                    if (!FeatureLocaleFilter.FeatureEnabled(row.GetAttribute("feature")) || !FeatureLocaleFilter.HasLocale(row.GetAttribute("locale"))) continue;
                    rows.Add((row.GetAttribute("type"), row.GetAttribute("key"), row.GetAttribute("value")));
                }
                break;
            }
            return rows;
        }

        Dictionary<string, string> ClientPriceRows() {
            // The data folder's pack first, then the played client's: a later row replaces an earlier one, so
            // the played client's row wins where it has one.
            var rows = new List<(string, string)>();
            foreach (M2dReader? reader in new[] { clientReader, playedReader }) {
                Maple2.File.IO.Crypto.Common.PackFileEntry? entry = reader?.Files.FirstOrDefault(file => file.Name == "table/constants.xml");
                if (reader == null || entry == null) {
                    continue;
                }
                foreach (XmlNode node in reader.GetXmlDocument(entry).SelectNodes("ms2/v")!) {
                    if (node is not XmlElement row) continue;
                    if (!FeatureLocaleFilter.FeatureEnabled(row.GetAttribute("feature")) || !FeatureLocaleFilter.HasLocale(row.GetAttribute("locale"))) continue;
                    rows.Add((row.GetAttribute("key"), row.GetAttribute("value")));
                }
            }
            return ClientPriceConstants.Select(rows);
        }

        string CleanInput(string input, string propName, Type type) {
            // check if string contains the ending 'f' for float designation, strip it if it does.
            if (type == typeof(float) && input.Contains('f')) {
                input = input.TrimEnd('f', 'F');
            }
            if (type == typeof(bool)) {
                // 1 does not automatically equate to true during bool conversion
                if (input == "1") {
                    input = "true";
                }
                // 0 does not automatically equate to false during bool conversion
                if (input == "0") {
                    input = "false";
                }
            }
            if (type == typeof(TimeSpan)) {
                // Special case - dashes (-) are used instead of colons (:)
                if (propName == "DailyTrophyResetDate") {
                    input = input.Replace('-', ':');
                }
                // Stored as 0.1 for 100ms
                if (propName == "GlobalCubeSkillIntervalTime") {
                    input = $"0:0:{input}";
                }
                // Stored as an int value, convert to friendly input string for TimeSpan parsing
                if (propName == "UgcHomeSaleWaitingTime") {
                    int.TryParse(input, out int result);
                    input = TimeSpan.FromSeconds(result).ToString(); // TODO: may not be correct conversion to TimeSpan
                }
            }
            if (type == typeof(int)) {
                // Remove prefix 0 on integers since they do not convert properly
                if (input.Length > 1 && input[0] == '0') {
                    input = input.Remove(0, 1);
                }
            }
            return input;
        }
    }
}
