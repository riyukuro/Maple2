using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Maple2.Database.Extensions;
using Maple2.Database.Storage;
using Maple2.Model;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Game.Dungeon;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Game.LuaFunctions;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using Maple2.Tools.Extensions;
using Serilog;

namespace Maple2.Server.Game.Manager;

public sealed class QuestManager {
    private const int BATCH_SIZE = 200;
    private readonly GameSession session;

    private readonly IDictionary<int, Quest> accountValues;
    private readonly IDictionary<int, Quest> characterValues;

    private ChangeJobMetadata? changeJobMetadata;

    private readonly HashSet<ConditionType> throttleStates = [
        ConditionType.fall, ConditionType.swim, ConditionType.swimtime, ConditionType.run, ConditionType.stay_cube,
        ConditionType.crawl, ConditionType.glide, ConditionType.climb, ConditionType.ropetime, ConditionType.laddertime,
        ConditionType.holdtime, ConditionType.riding, ConditionType.emotiontime,
    ];

    private readonly ILogger logger = Log.Logger.ForContext<QuestManager>();

    // The alliance mission lists last sent (AllianceMissions, Quest 0x1F and 0x20) and the period each holds.
    private readonly HashSet<int> dailyMissions = [];
    private readonly HashSet<int> weeklyMissions = [];
    private DateTime dailyMissionsStart;
    private DateTime weeklyMissionsStart;
    // The missions (AllianceMissions.IsMission) and their weekly reset day, read once.
    private List<QuestMetadata>? missionMetadata;
    private int missionWeekday;

    public QuestManager(GameSession session) {
        this.session = session;

        using GameStorage.Request db = session.GameStorage.Context();
        accountValues = db.GetQuests(session.AccountId);
        characterValues = db.GetQuests(session.CharacterId);
    }

    // The auto start quests the player can start now. Called at login once the guild is loaded: CanStart reads the
    // guild's quest npc level (require guildNpcLevel), and EnterServer makes the guild manager after this one.
    public void StartAutoQuests() {
        using GameStorage.Request db = session.GameStorage.Context();
        Initialize(db);
    }

    public void Load() {
        DropEndedMissions(DateTime.Now);
        DropDailyQuests(DateTime.Now, false);
        // The held items are counted again on login (a counter stored before held counts, or items moved since).
        foreach (Quest quest in HeldCandidates(AnyHeld)) {
            CountHeld(quest, AnyHeld);
        }
        session.Send(QuestPacket.LoadExploration(session.Config.ExplorationProgress, MissionAttackCount()));

        // A stored quest the played client lacks stays stored but is not sent: the client would read the list wrong
        // from it on (PlayedClientQuests).
        foreach (ImmutableList<Quest> batch in accountValues.Values.Where(quest => PlayedClientQuests.Known(quest.Metadata.Basic)).Batch(BATCH_SIZE)) {
            session.Send(QuestPacket.LoadQuestStates(batch));
        }
        foreach (ImmutableList<Quest> batch in characterValues.Values.Where(quest => PlayedClientQuests.Known(quest.Metadata.Basic)).Batch(BATCH_SIZE)) {
            session.Send(QuestPacket.LoadQuestStates(batch));
        }
    }

    // The quests of the live stamp cards (StampEvent). They are event missions with an event tag that no quest tag
    // event carries; the card is their event, so the server starts them while it is live, and completes them
    // when their conditions are met: the card's dialog has no action for either, it only shows a stamp as done
    // once its quest is rewarded.
    private HashSet<int> StampQuests() {
        return stampQuests ??= StampEventRules.Quests(session.FindEvent(GameEventType.StampEvent)
            .Select(gameEvent => gameEvent.Metadata.Data).OfType<StampEvent>());
    }
    private HashSet<int>? stampQuests;

    // A stamp quest is completed on the update that fills its last counter. When that completion fails (a full
    // inventory), or the quest starts with nothing left to count, no later update comes: every started stamp quest
    // that can be completed is tried again here, on each field entry and before a claim on the card.
    public void CompleteStampQuests() {
        if (StampQuests().Count == 0) {
            return;
        }
        foreach (Quest quest in accountValues.Values.Concat(characterValues.Values).ToList()) {
            if (quest.State == QuestState.Started && StampQuests().Contains(quest.Id) && CanComplete(quest)) {
                Complete(quest);
            }
        }
    }

    private void Initialize(GameStorage.Request db) {
        IEnumerable<QuestMetadata> quests = session.QuestMetadata.GetQuests();
        IList<QuestTag> events = session.FindEvent(GameEventType.QuestTag)
            .Where(gameEvent => gameEvent.Metadata.Data is QuestTag)
            .Select(gameEvent => (QuestTag) gameEvent.Metadata.Data)
            .ToList();
        foreach (QuestMetadata metadata in quests) {
            // A live card's quest is started whether or not it is an auto start quest (the replayed card's are).
            bool stampQuest = StampQuests().Contains(metadata.Id);
            if ((!metadata.Basic.AutoStart && !stampQuest) ||
                metadata.Basic.Type == QuestType.FieldMission) {
                continue;
            }

            // Event missions should only be started through the event.
            if (metadata.EventMissionType != QuestEventMissionType.none && !stampQuest) {
                continue;
            }

            if (metadata.Basic.CompleteNpc != 0 && !stampQuest) {
                continue;
            }

            if (metadata.Basic.CompleteMaps != null && metadata.Basic.CompleteMaps.Length != 0 && !stampQuest) {
                continue;
            }

            if (!CanStart(metadata)) {
                continue;
            }

            if (characterValues.ContainsKey(metadata.Id) || accountValues.ContainsKey(metadata.Id)) {
                continue;
            }

            var quest = new Quest(metadata) {
                Track = true,
                State = QuestState.Started,
                StartTime = DateTime.Now.ToEpochSeconds(),
            };

            for (int i = 0; i < metadata.Conditions.Length; i++) {
                quest.Conditions.Add(i, new Quest.Condition(metadata.Conditions[i]));
            }
            QuestRules.CountLevelAtStart(quest.Conditions.Values, session.Player.Value.Character.Level);

            long ownerId = metadata.Basic.Account > 0 ? session.AccountId : session.CharacterId;
            quest = db.CreateQuest(ownerId, quest);
            if (quest == null) {
                continue;
            }

            if (metadata.Basic.Account > 0) {
                accountValues.Add(metadata.Id, quest);
                continue;
            }
            Add(quest);
            session.Send(QuestPacket.Start(quest));
        }
    }

    /// <summary>
    /// Adds a quest to the quest manager.
    /// </summary>
    /// <param name="quest">Quest to add</param>
    private void Add(Quest quest) {
        if (quest.Metadata.Basic.Account > 0) {
            accountValues.Add(quest.Id, quest);
            return;
        }
        characterValues.Add(quest.Id, quest);
    }

    /// <summary>
    /// Starts a new quest (or prexisting if repeatable).
    /// </summary>
    public QuestError Start(int questId, bool bypassRequirements = false) {
        // A quest already held is started again only when it is completed and its repeat allows it (CanStartAgain).
        TryGetQuest(questId, out Quest? held);
        if (held != null && !CanStartAgain(held)) {
            return QuestError.s_quest_error_accept_fail;
        }

        // Not even with bypassRequirements: the played client has no such quest.
        if (!session.QuestMetadata.TryGet(questId, out QuestMetadata? metadata) || !PlayedClientQuests.Known(metadata.Basic)) {
            return QuestError.s_quest_error_accept_fail;
        }

        if (!bypassRequirements && !CanStart(metadata)) {
            return QuestError.s_quest_error_accept_fail;
        }

        // An alliance mission only when it is in the list the client was sent (CQuestManager::vfunc_1).
        if (!bypassRequirements && !AllianceMissions.Listed(metadata, dailyMissions, weeklyMissions)) {
            return QuestError.s_quest_error_accept_fail;
        }

        // The quest's items must fit the bag: without them the quest could not be done. A forced start (a map
        // script's, a command's) is not refused: the items that do not fit go by mail.
        List<Item> acceptItems = [];
        foreach (QuestMetadataReward.Item acceptReward in metadata.AcceptReward.EssentialItem) {
            Item? reward = session.Field?.ItemDrop.CreateItem(acceptReward.Id, acceptReward.Rarity, acceptReward.Amount);
            if (reward == null) {
                logger.Error("Failed to create quest reward {RewardId}", acceptReward.Id);
                continue;
            }
            acceptItems.Add(reward);
        }
        if (!bypassRequirements && acceptItems.Count > 0 && !session.Item.Inventory.CanAdd(acceptItems)) {
            return QuestError.s_quest_error_inventory_full;
        }

        Quest? quest;
        if (held != null) {
            // The stored row is reused: started again with fresh conditions, its completion count kept. The client
            // takes the Start packet as a new quest in place of the completed one. The times of the last completion
            // are kept beside the new ones for an abandon.
            quest = held;
            quest.LastStartTime = quest.StartTime;
            quest.LastEndTime = quest.EndTime;
            quest.Track = true;
            quest.State = QuestState.Started;
            quest.StartTime = DateTime.Now.ToEpochSeconds();
            quest.EndTime = 0;
            quest.Conditions.Clear();
            for (int i = 0; i < metadata.Conditions.Length; i++) {
                quest.Conditions.Add(i, new Quest.Condition(metadata.Conditions[i]));
            }
            QuestRules.CountLevelAtStart(quest.Conditions.Values, session.Player.Value.Character.Level);
        } else {
            quest = new Quest(metadata) {
                Track = true,
                State = QuestState.Started,
                StartTime = DateTime.Now.ToEpochSeconds(),
            };

            for (int i = 0; i < metadata.Conditions.Length; i++) {
                quest.Conditions.Add(i, new Quest.Condition(metadata.Conditions[i]));
            }
            QuestRules.CountLevelAtStart(quest.Conditions.Values, session.Player.Value.Character.Level);

            using GameStorage.Request db = session.GameStorage.Context();
            long ownerId = metadata.Basic.Account > 0 ? session.AccountId : session.CharacterId;
            quest = db.CreateQuest(ownerId, quest);
            if (quest == null) {
                logger.Error("Failed to create quest entry {questId}", metadata.Id);
                return QuestError.s_quest_error_accept_fail;
            }
            Add(quest);
        }

        foreach (Item reward in acceptItems) {
            if (!session.Item.AddOrMail(reward, true)) {
                logger.Error("Failed to give quest reward {RewardId}", reward.Id);
            }
        }

        session.ConditionUpdate(ConditionType.quest_accept, codeLong: quest.Id);
        // Items, meso and worn items held before the quest count from its start, as the client shows them.
        CountHeld(quest, AnyHeld);
        session.Send(QuestPacket.Start(quest));
        if (quest.Metadata.SummonPortal != null) {
            SummonPortal(quest);
        }
        if (session.TableMetadata.FameTable.PickMethods.Any(method => method.ReloadOnAccept == quest.Id)) {
            LoadAllianceMissions();
        }
        return QuestError.none;
    }

    /// <summary>
    /// Updates all possible quests with the given condition type.
    /// </summary>
    /// <param name="type">Condition Type to update</param>
    /// <param name="counter">Condition value to progress by. Default is 1.</param>
    /// <param name="targetString">condition target parameter in string.</param>
    /// <param name="targetLong">condition target parameter in long.</param>
    /// <param name="codeString">condition code parameter in string.</param>
    /// <param name="codeLong">condition code parameter in long.</param>
    public void Update(ConditionType type, long counter = 1, string targetString = "", long targetLong = 0, string codeString = "", long codeLong = 0) {
        IEnumerable<Quest> quests = QuestRules.UpdateCandidates(characterValues.Values.Concat(accountValues.Values), type);
        foreach (Quest quest in quests) {
            // TODO: Not sure if ProgressMap really means that only progress counts in this map. It doesn't make sense for some quests.
            // Testing only on FieldMission for now.
            if (quest.Metadata.Basic is { Type: QuestType.FieldMission, ProgressMaps: not null } && session.Field is not null) {
                switch (session.Field.Metadata.Property.ExploreType) {
                    case 1 or 0 when !quest.Metadata.Basic.ProgressMaps.Contains(session.Player.Value.Character.MapId):
                    case 2 when !quest.Metadata.Basic.ProgressMaps.Any(x => session.Player.Value.Character.ReturnMaps.Contains(x)):
                        continue;
                }
            }

            if (quest.Metadata.Basic.Type == QuestType.MentoringMission && quest.Metadata.Mentoring != null) {
                // if the required opening day hasn't passed yet, skip
                if ((DateTime.Now - quest.StartTime.FromEpochSeconds()).TotalDays < quest.Metadata.Mentoring.OpeningDay) {
                    continue;
                }
            }

            foreach (Quest.Condition condition in quest.Conditions.Values.Where(condition => condition.Metadata.Type == type)) {
                // Already meets the requirement and does not need to be updated
                if (condition.Counter >= condition.Metadata.Value) {
                    continue;
                }

                if (!condition.Metadata.Check(session, targetString, targetLong, codeString, codeLong)) {
                    continue;
                }

                if (type is ConditionType.interior_point && !InteriorPointRules.Advances(condition.Counter, counter)) {
                    continue;
                }

                int previousCounter = condition.Counter;
                long counted = type is ConditionType.interior_point
                    ? InteriorPointRules.Counter(condition.Counter, counter)
                    : condition.Counter + counter;
                condition.Counter = (int) Math.Min(condition.Metadata.Value, counted);

                // Only send update every 5 increments for throttleStates
                if (throttleStates.Contains(type)) {
                    if (condition.Counter / 5 > previousCounter / 5 || condition.Counter == condition.Metadata.Value) {
                        session.Send(QuestPacket.Update(quest));
                    }
                } else {
                    session.Send(QuestPacket.Update(quest));
                }

                if ((quest.Metadata.Basic.Type == QuestType.FieldMission || StampQuests().Contains(quest.Id)) && CanComplete(quest)) {
                    Complete(quest);
                }
            }
        }
    }

    // Quest sub-type 28: the player used the quest action on another player. Each user_find condition of the
    // started quest that is not full yet and that the found player meets goes up by one.
    public bool UpdateUserFind(int questId, in QuestRules.FoundUser user) {
        if (!TryGetQuest(questId, out Quest? quest) || quest.State != QuestState.Started || !PlayedClientQuests.Known(quest.Metadata.Basic)) {
            return false;
        }

        bool changed = false;
        foreach (Quest.Condition condition in quest.Conditions.Values) {
            if (condition.Metadata.Type != ConditionType.user_find || condition.Counter >= condition.Metadata.Value) {
                continue;
            }
            if (!QuestRules.MatchesUserFind(condition.Metadata.Target?.Strings, user)) {
                continue;
            }

            condition.Counter++;
            changed = true;
        }

        if (changed) {
            session.Send(QuestPacket.Update(quest));
        }
        return changed;
    }

    /// <summary>
    /// Checks if player can start a quest.
    /// </summary>
    /// <param name="metadata">Metadata of the quest.</param>
    public bool CanStart(QuestMetadata metadata) {
        if (metadata.Basic.Disabled || !PlayedClientQuests.Known(metadata.Basic)) {
            return false;
        }

        if (metadata.Basic.EventTag != string.Empty && !StampQuests().Contains(metadata.Id) &&
            !session.FindEvent(GameEventType.QuestTag).Any(gameEvent => gameEvent.Metadata.Data is QuestTag tag && tag.Tag == metadata.Basic.EventTag)) {
            return false;
        }

        QuestMetadataRequire require = metadata.Require;
        if (require.Level > 0 && require.Level > session.Player.Value.Character.Level) {
            return false;
        }

        if (require.MaxLevel > 0 && require.MaxLevel < session.Player.Value.Character.Level) {
            return false;
        }

        if (require.Job.Length > 0 && !require.Job.Contains(session.Player.Value.Character.Job.Code())) {
            return false;
        }

        if (require.GearScore > session.Stats.Values.GearScore) {
            return false;
        }

        IDictionary<AllianceType, int> famePoints = session.Player.Value.Unlock.FamePoints;
        if (!QuestRules.MeetsGrades(require,
                alliance => FameLogRules.Grade(session.TableMetadata.FameTable, alliance, famePoints.GetValueOrDefault(alliance)),
                type => session.Mastery.GetLevel(type))) {
            return false;
        }

        if (require.Achievement > 0 && !session.Achievement.TryGetAchievement(require.Achievement, out _)) {
            return false;
        }

        if (require.UnrequiredAchievement != (0, 0)
            && session.Achievement.TryGetAchievement(require.UnrequiredAchievement.Item1, out Achievement? achievement)
            && achievement.Grades.ContainsKey(require.UnrequiredAchievement.Item2)) {
            return false;
        }

        if (require.Quest.Length > 0) {
            foreach (int questId in require.Quest) {
                if (!TryGetQuest(questId, out Quest? requiredQuest) || requiredQuest.State != QuestState.Completed) {
                    return false;
                }
            }
        }

        // The requires below gate accepting only: a quest already held stays held when one stops holding later (the
        // guild's quest npc changes level, the player leaves the guild, the day rolls over), as the client keeps it.
        // progressQuest and unrequire (questdata.cpp:501-517): the client's IsQuestActive is state 1 (started) and
        // IsQuestRewarded state 2 (completed).
        if (require.ProgressQuest != null && require.ProgressQuest.Any(questId => questId != 0 && !HasState(questId, QuestState.Started))) {
            return false;
        }

        if (require.Unrequire != null && require.Unrequire.Any(questId => questId != 0
                && (HasState(questId, QuestState.Started) || HasState(questId, QuestState.Completed)))) {
            return false;
        }

        if (!QuestRules.MeetsGuildNpcLevel(require.GuildNpcLevel, session.Guild.Guild != null, session.Guild.NpcLevel(GuildNpcType.Quest))) {
            return false;
        }

        if (!QuestRules.OnWeekday(require.Weekdays, DateTime.Now)) {
            return false;
        }

        if (!MeetsDungeonRequires(require)) {
            return false;
        }

        if (require.GroupId != 0 && !GroupPicks(require.GroupId, DateTime.Now).Contains(metadata.Id)) {
            return false;
        }

        if (require.UnrequireGroup != null && !UnrequiredGroupsFree(require.UnrequireGroup, DateTime.Now)) {
            return false;
        }

        return require.SelectableQuest.Length <= 0 || SelectableQuests(require.SelectableQuest);
    }

    // The dungeon requires (CanAcceptQuest, questdata.cpp:588-607). The clear count is the record's TotalClears, the
    // value the client reads (DungeonRecordManagerCli::GetRoomValue5c, dungeonrecordcli.cpp:597, the m_5c the record
    // packet carries in TotalClears' place; no record reads 0). The enter state is the one FieldEntrancePacket sends
    // (CMyCharacter::GetDungeonEnterState, notifydebugstate.cpp:257; a room not sent reads 0).
    private bool MeetsDungeonRequires(QuestMetadataRequire require) {
        if (require.Dungeon != 0 && DungeonClears(require.Dungeon) < 1) {
            return false;
        }
        if (require.UnrequireDungeon != 0 && DungeonClears(require.UnrequireDungeon) != 0) {
            return false;
        }
        if (require.EnterDungeon != 0 && session.Player.Value.Character.DungeonEnterLimits.TryGetValue(require.EnterDungeon, out DungeonEnterLimit limit)
                && (byte) limit >= 10) {
            return false;
        }
        if (require.DungeonBeginner != null && require.DungeonBeginner.Length > 0 && require.DungeonBeginner.All(CanClaimHelperReward)) {
            return false;
        }
        return true;
    }

    private int DungeonClears(int dungeonId) =>
        session.Dungeon.Records.TryGetValue(dungeonId, out DungeonRecord? record) ? record.TotalClears : 0;

    // A room the table lacks reads false (CanClaimHelperReward, dungeonrecordcli.cpp:509).
    private bool CanClaimHelperReward(int dungeonId) =>
        session.TableMetadata.DungeonRoomTable.Entries.TryGetValue(dungeonId, out DungeonRoomMetadata? room)
        && DungeonRoomRules.CanClaimHelperReward(session.Dungeon.Records.TryGetValue(dungeonId, out DungeonRecord? record) ? record : null,
            room.HelperRequireClearCount);

    private bool HasState(int questId, QuestState state) => TryGetQuest(questId, out Quest? quest) && quest.State == state;

    // CQuestManager::CanEnterSkyFortress (questmanager_2.cpp:1723), the server's part: the quest the sky fortress
    // season (1) requires is rewarded (IsQuestRewarded, state 2), and the player is not on the target field already.
    // A table stored before the seasons were read has none: the constant then names the same quest (91000013 in KR).
    // Chosen here: the client's third check (the player on the home field 62000000 or FUN_1409312f0) is left to it.
    public bool CanEnterSkyFortress(int mapId) {
        FameTable fame = session.TableMetadata.FameTable;
        int questId = fame.SeasonRequireQuests == null ? session.ServerTableMetadata.ConstantsTable.FameContentsRequireQuestID : fame.SeasonRequireQuest(1);
        return HasState(questId, QuestState.Completed) && session.Field?.MapId != mapId;
    }

    // CQuestManager::FUN_1416d0800 (questmanager_2.cpp:1706): the alliance of the first quest in progress among the
    // field quests of the field (QuestDataTable::FUN_14074d360, by the quest's first progress map); 0 when none.
    // Chosen here: the server takes the held quests in id order.
    public AllianceType FieldQuestAlliance(int mapId) {
        foreach (Quest quest in characterValues.Values.Concat(accountValues.Values).OrderBy(quest => quest.Id)) {
            QuestMetadataBasic basic = quest.Metadata.Basic;
            if (quest.State != QuestState.Started || basic.Type != QuestType.FieldQuest || basic.ProgressMaps is not { Length: > 0 } maps || maps[0] != mapId) {
                continue;
            }
            return basic.Alliance;
        }
        return AllianceType.AnyFaction;
    }

    // The sky fortress mission attacks made today (Quest 0x0F): 0 once a daily reset has passed since the count was
    // stored.
    public int MissionAttackCount() {
        Character character = session.Player.Value.Character;
        return PetFusionRules.DailyCount(character.MissionAttackCount, character.MissionAttackTime, PetFusionRules.LastResetTime());
    }

    // CQuestManager::SendQuestMode15 (questmanager_2.cpp:1766) checks, in its order: the move to the field is allowed
    // (CanEnterSkyFortress), a field quest of the field with an alliance is in progress, and fewer than
    // FameContentsMissionAttackCount attacks were made today.
    public bool CanMissionAttack(int mapId) {
        return CanEnterSkyFortress(mapId)
            && FieldQuestAlliance(mapId) != AllianceType.AnyFaction
            && MissionAttackCount() < session.ServerTableMetadata.ConstantsTable.FameContentsMissionAttackCount;
    }

    // One more attack today, after the move is prepared, and the trophy condition mission_attack_system: the trophy
    // 22500041's description (achievedescription.xml manualDesc) counts the uses of the sky fortress sortie system.
    public void CountMissionAttack() {
        Character character = session.Player.Value.Character;
        character.MissionAttackCount = MissionAttackCount() + 1;
        character.MissionAttackTime = DateTime.Now.ToEpochSeconds();
        session.ConditionUpdate(ConditionType.mission_attack_system);
    }

    // The daily reset of the attacks: 0x10B clears the client's count (ResetRefreshCount, questmanager_2.cpp:766).
    // Chosen here: 0x10B is sent at the daily reset; the client also opens its daily mission window on it.
    public void ResetMissionAttacks() {
        Character character = session.Player.Value.Character;
        character.MissionAttackCount = 0;
        character.MissionAttackTime = DateTime.Now.ToEpochSeconds();
        session.Send(QuestPacket.DailyReset());
    }

    // The quests of a group the client accepts now (QuestGroupPick): the pick over the group's quests in id order,
    // seeded by the first one's repeat.
    public List<int> GroupPicks(int groupId, DateTime now) {
        IReadOnlyList<int> ids = session.QuestMetadata.GetGroup(groupId);
        if (ids.Count == 0 || !session.QuestMetadata.TryGet(ids[0], out QuestMetadata? first)) {
            return [];
        }
        return QuestGroupPick.Pick(ids, first.Basic.Repeat, groupId, now);
    }

    // unrequireGroup (CanAcceptQuest, questdata.cpp:520): no picked quest of the groups is in progress, and none is
    // completed unless it may be started again (the client's vfunc_o10_21 on it; CanStartAgain is its repeat rule).
    private bool UnrequiredGroupsFree(IEnumerable<int> groupIds, DateTime now) {
        foreach (int groupId in groupIds) {
            foreach (int questId in GroupPicks(groupId, now)) {
                if (TryGetQuest(questId, out Quest? quest) && (quest.State == QuestState.Started
                                                                || quest.State == QuestState.Completed && !CanStartAgain(quest))) {
                    return false;
                }
            }
        }
        return true;
    }

    /// <summary>
    /// Finds if any of the quests in the list are completed.
    /// </summary>
    /// <returns>Returns true if at least one quest has been completed by the player.</returns>
    private bool SelectableQuests(IEnumerable<int> questIds) {
        foreach (int questId in questIds) {
            if (TryGetQuest(questId, out Quest? quest) && quest.State == QuestState.Completed) {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// A completed quest whose repeat lets it be started again now (QuestRules.CanStartAgain). Not a guild quest: the
    /// client adds a wait of a day or a week from a time the server does not keep (questdata.cpp:399).
    /// </summary>
    public static bool CanStartAgain(Quest quest) {
        return quest.State == QuestState.Completed && quest.Metadata.Basic.Type != QuestType.GuildQuest
               && QuestRules.CanStartAgain(quest.Metadata.Basic.Repeat, quest.Metadata.Basic.ResetWeekday, quest.StartTime, DateTime.Now);
    }

    // QuestRules.IsExpired with the quest's term: the QuestTag event of its tag. A stamp quest is started without one
    // (CanStart), so its tag sets no end.
    public bool IsExpired(Quest quest) {
        QuestMetadataBasic basic = quest.Metadata.Basic;
        if (!string.IsNullOrEmpty(basic.EventTag) && StampQuests().Contains(quest.Id)) {
            basic = basic with { EventTag = string.Empty };
        }
        (long, long)? term = null;
        if (!string.IsNullOrEmpty(basic.EventTag)) {
            GameEvent? gameEvent = session.FindEvent(GameEventType.QuestTag)
                .FirstOrDefault(gameEvent => gameEvent.Metadata.Data is QuestTag tag && tag.Tag == basic.EventTag);
            if (gameEvent != null) {
                term = (gameEvent.StartTime, gameEvent.EndTime);
            }
        }
        return QuestRules.IsExpired(basic, quest.StartTime, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), term);
    }

    public bool CanComplete(Quest quest) {
        return quest.State != QuestState.Completed && quest.Conditions
            .All(condition => condition.Value.Counter >= condition.Value.Metadata.Value);
    }

    /// <summary>
    /// Quest 0x18, the claim button of the daily mission page and the level guide: completes the held quest when the
    /// client and the server agree it is due (QuestRules.RemoteClaimCheck), else says why.
    /// </summary>
    public QuestRules.RemoteClaim RemoteComplete(int questId) {
        TryGetQuest(questId, out Quest? quest);
        QuestRules.RemoteClaim claim = QuestRules.RemoteClaimCheck(session.QuestMetadata.TryGet(questId, out _), quest,
            quest != null && PlayedClientQuests.Known(quest.Metadata.Basic), quest != null && CanComplete(quest));
        if (claim != QuestRules.RemoteClaim.Done) {
            return claim;
        }

        // The checks above leave Complete only the full inventory to refuse (it answers that itself).
        return Complete(quest!) ? QuestRules.RemoteClaim.Done : QuestRules.RemoteClaim.InventoryFull;
    }

    /// <summary>
    /// Gives the player the rewards for completing the quest.
    /// </summary>
    public bool Complete(Quest quest, bool bypassConditions = false) {
        // A quest the played client lacks is not completed (no reward, no packet), also not with bypassConditions.
        if (quest.State == QuestState.Completed || !PlayedClientQuests.Known(quest.Metadata.Basic)) {
            return false;
        }

        if (!bypassConditions && !quest.Conditions.All(condition => condition.Value.Counter >= condition.Value.Metadata.Value)) {
            return false;
        }

        QuestMetadataReward reward = quest.Metadata.CompleteReward;

        List<Item> rewards = [];
        foreach (QuestMetadataReward.Item entry in reward.EssentialItem) {
            Item? item = session.Field?.ItemDrop.CreateItem(entry.Id, entry.Rarity, entry.Amount);
            if (item is null) {
                continue;
            }

            rewards.Add(item);
        }

        foreach (QuestMetadataReward.Item entry in reward.EssentialJobItem) {
            if (!session.ItemMetadata.TryGet(entry.Id, out ItemMetadata? metadata)) {
                continue;
            }

            if (metadata.Limit.JobRecommends.Length > 0 && !metadata.Limit.JobRecommends.Contains(JobCode.None) && !metadata.Limit.JobRecommends.Contains(session.Player.Value.Character.Job.Code())) {
                continue;
            }

            Item? item = session.Field?.ItemDrop.CreateItem(entry.Id, entry.Rarity, entry.Amount);
            if (item is null) {
                continue;
            }

            rewards.Add(item);
        }

        // A guild quest's reward gets the bonus of the guild's quest npc level, as the reward line shows it.
        GuildTable.QuestRewardFactor? guildFactor = quest.Metadata.Basic.Type == QuestType.GuildQuest ? session.Guild.QuestRewardFactor() : null;

        // completeReward guildCoin: the guild coin item, as the check-in gives it.
        int guildCoinCount = guildFactor == null ? reward.GuildCoin : (int) GuildRules.QuestRewardBonus(reward.GuildCoin, guildFactor.GuildCoin);
        if (guildCoinCount > 0) {
            Item? guildCoin = session.Field?.ItemDrop.CreateItem(Constant.GuildCoinId, Constant.GuildCoinRarity, guildCoinCount);
            if (guildCoin != null) {
                rewards.Add(guildCoin);
            }
        }

        if (!session.Item.Inventory.CanAdd(rewards) && !Constant.MailQuestItems) {
            session.Send(ItemInventoryPacket.Error(ItemInventoryError.s_err_inventory));
            return false;
        }

        // The order KMS2 sends: the quest's complete packet, the meso, the exp, the items, then what counts the quest.
        quest.EndTime = DateTime.Now.ToEpochSeconds();
        quest.LastStartTime = 0;
        quest.LastEndTime = 0;
        quest.State = QuestState.Completed;
        quest.CompletionCount++;
        session.Send(QuestPacket.Complete(quest));
        session.AdventureProtect.Update();

        // The meso the reward line shows: a guild quest's relativeMeso row at the player's level with the guild bonus,
        // else the fixed money.
        long? relativeMeso = reward.RelativeMeso != 0
            ? session.TableMetadata.RewardContentTable.RewardMeso(reward.RelativeMeso, session.Player.Value.Character.Level)
            : null;
        long meso = QuestMesoRules.Meso(reward.Meso, relativeMeso, session.ServerTableMetadata.ConstantsTable.QuestRewardMesoFactor(quest.Metadata.Basic.Type),
            quest.Metadata.Basic.Type == QuestType.GuildQuest ? guildFactor?.UserMeso : null);
        if (meso > 0) {
            session.Currency.Meso += meso;
        }

        // The exp the reward line shows: the fixed exp or the relativeExp curve at the player's level, whichever is
        // larger, with the rank factor of field and alliance quests.
        QuestMetadataBasic basic = quest.Metadata.Basic;
        long exp = QuestExpRules.Exp(reward.Exp, session.Exp.TypeValue(reward.RelativeExp), basic.Type, basic.Rank, (int) basic.Alliance);
        if (guildFactor != null) {
            exp = GuildRules.QuestRewardBonus(exp, guildFactor.UserExp);
        }
        if (exp > 0) {
            session.Exp.AddQuestExp(exp, QuestExpRules.Reason(basic.Type, basic.StandardLevel).Message(), QuestExpRules.PrestigeType(basic.Type));
        }

        if (reward.Treva > 0) {
            session.Currency[CurrencyType.Treva] += reward.Treva;
        }

        if (reward.Rue > 0) {
            session.Currency[CurrencyType.Rue] += reward.Rue;
        }

        // menteeCoin: the mentee token the mentoring page shows on the reward line (FUN_140762be0,
        // commonfiles/questmasktable.cpp:1000, no bonus); the currency caps it and counts get_mentee_token.
        if (reward.MenteeCoin > 0) {
            session.Currency[CurrencyType.MenteeToken] += reward.MenteeCoin;
        }

        // weddingExp: the wedding mission's intimacy, once for each partner who completes it.
        session.Marriage.AddWeddingMissionExp(reward.WeddingExp);

        int fame = AllianceMissions.FameReward(quest.Metadata);
        if (fame > 0) {
            AddFamePoints(quest.Metadata.Basic.Alliance, fame);
        }

        foreach (Item item in rewards) {
            if (!session.Item.Inventory.Add(item, true)) {
                session.Item.MailItem(item);
            }
        }

        if (guildFactor != null) {
            session.Guild.AddExpFund(GuildRules.QuestRewardBonus(reward.GuildExp, guildFactor.GuildExp),
                GuildRules.QuestRewardBonus(reward.GuildFund, guildFactor.GuildFund));
        } else {
            session.Guild.AddExpFund(reward.GuildExp, reward.GuildFund);
        }

        int missionPoints = MissionPointRules.PointsCounted(reward.MissionPoint);
        if (missionPoints > 0) {
            session.ConditionUpdate(ConditionType.mission_point, counter: missionPoints);
        }
        session.ConditionUpdate(ConditionType.quest_clear_by_chapter, codeLong: quest.Metadata.Basic.ChapterId);
        session.ConditionUpdate(ConditionType.quest, codeLong: quest.Metadata.Id);
        session.ConditionUpdate(ConditionType.quest_clear, codeLong: quest.Metadata.Id);
        // quest_revise_achieve (trophies 22210017, 22300102): completing a quest of the condition's code adds 1, as
        // KMS2 sends right after the quest's complete packet.
        session.ConditionUpdate(ConditionType.quest_revise_achieve, codeLong: quest.Metadata.Id);
        if (QuestRules.CountsAsDaily(quest.Metadata.Basic)) {
            session.ConditionUpdate(ConditionType.quest_daily, codeLong: quest.Metadata.Id);
        }
        if (QuestRules.CountsAsGuide(quest.Metadata.Basic)) {
            session.ConditionUpdate(ConditionType.quest_guide);
        }
        if (quest.Metadata.Basic.Type == QuestType.FieldMission) {
            session.ConditionUpdate(ConditionType.field_mission, codeLong: quest.Metadata.Id);
        }
        if (QuestRules.CountsAsAllianceMission(quest.Metadata.Basic)) {
            session.ConditionUpdate(ConditionType.quest_alliance, codeLong: (long) quest.Metadata.Basic.Alliance);
        }
        if (QuestRules.AllianceRankLetter(quest.Metadata.Basic) is { } rankLetter) {
            session.ConditionUpdate(ConditionType.quest_alliance_by_grade, codeString: rankLetter,
                targetLong: (long) quest.Metadata.Basic.Alliance);
        }
        if (QuestRules.CountsAsAllianceFieldMission(quest.Metadata.Basic)) {
            session.ConditionUpdate(ConditionType.quest_field, codeLong: (long) quest.Metadata.Basic.Alliance);
        }

        TryJobAdvance(quest.Id);
        CompleteChapter(quest.Id);
        // The quest that opens the alliance missions (the methods' reloadOnComplete) makes the lists again.
        if (session.TableMetadata.FameTable.PickMethods.Any(method => method.ReloadOnComplete == quest.Id)) {
            LoadAllianceMissions();
        }
        return true;
    }

    /// <summary>
    /// Sends the day's and the week's alliance missions (Quest 0x1F and 0x20), at login and when the quest that opens
    /// them is completed. The held missions stay. A period the reset already started is kept.
    /// </summary>
    public void LoadAllianceMissions() {
        DateTime now = DateTime.Now;
        DateTime daily = AllianceMissions.PeriodStart(2, 0, now);
        DateTime weekly = AllianceMissions.PeriodStart(3, MissionWeekday(), now);
        SendAllianceMissions(2, daily > dailyMissionsStart ? daily : dailyMissionsStart, false);
        SendAllianceMissions(3, weekly > weeklyMissionsStart ? weekly : weeklyMissionsStart, false);
    }

    /// <summary>
    /// At the daily reset (on the session's own queue, GameSession.DailyReset): a list whose period ended is picked
    /// again, and the held missions of that repeat that are not completed are dropped, on the client by the list's
    /// drop flag (RemoveQuestsByTypeAndState). A list whose period goes on is sent again as it is. The period is the
    /// one the reset starts (AllianceMissions.ResetPeriodStart), so a reset a moment before midnight still starts it.
    /// </summary>
    public void ResetAllianceMissions(DateTime now) {
        DateTime daily = AllianceMissions.ResetPeriodStart(2, 0, now);
        DateTime weekly = AllianceMissions.ResetPeriodStart(3, MissionWeekday(), now);
        SendAllianceMissions(2, daily, daily != dailyMissionsStart);
        SendAllianceMissions(3, weekly, weekly != weeklyMissionsStart);
    }

    private void SendAllianceMissions(int repeat, DateTime start, bool drop) {
        if (drop) {
            DropMissions(repeat, start);
        }

        // Each mission of the repeat: held when started in the period (completed or not), else eligible when the
        // player can start it (AllianceMissions.Pick).
        long startSeconds = start.ToEpochSeconds();
        List<AllianceMissions.Candidate> candidates = Missions()
            .Where(metadata => metadata.Basic.Repeat == repeat)
            .Select(metadata => {
                bool held = TryGetQuest(metadata.Id, out Quest? quest) && quest.StartTime >= startSeconds;
                return new AllianceMissions.Candidate(metadata.Id, metadata.Basic.Alliance, metadata.Basic.Rank, held,
                    !held && CanStart(metadata));
            })
            .ToList();
        HashSet<int> picks = repeat == 2 ? dailyMissions : weeklyMissions;
        picks.Clear();
        IReadOnlyList<FameTable.PickMethod> methods = session.TableMetadata.FameTable.PickMethods;
        for (int i = 0; i < methods.Count; i++) {
            if (methods[i].Repeat == repeat) {
                picks.UnionWith(AllianceMissions.Pick(methods[i], candidates, AllianceMissions.Seed(session.CharacterId, start, i)));
            }
        }
        if (repeat == 2) {
            dailyMissionsStart = start;
            session.Send(QuestPacket.DailyAllianceMissions(drop, picks));
        } else {
            weeklyMissionsStart = start;
            session.Send(QuestPacket.WeeklyAllianceMissions(drop, picks));
        }
    }

    /// <summary>
    /// Accept all (Quest 34): starts each of the alliance's missions in the lists sent that can be started.
    /// </summary>
    /// <returns>The number of quests started.</returns>
    public int StartAllianceMissions(AllianceType alliance) {
        IEnumerable<QuestMetadata> listed = dailyMissions.Concat(weeklyMissions)
            .Select(questId => session.QuestMetadata.TryGet(questId, out QuestMetadata? metadata) ? metadata : null)
            .OfType<QuestMetadata>();
        int started = 0;
        foreach (int questId in AllianceMissions.AcceptAll(listed, alliance).ToList()) {
            if (Start(questId) == QuestError.none) {
                started++;
            }
        }
        return started;
    }

    /// <summary>
    /// Complete all (Quest 35): completes each held alliance quest of the alliance of repeat 2 or 3 whose conditions are
    /// done (CanCompleteAllQuests).
    /// </summary>
    /// <returns>The number of quests completed.</returns>
    public int CompleteAllianceQuests(AllianceType alliance) {
        int completed = 0;
        foreach (Quest quest in characterValues.Values.Concat(accountValues.Values).OrderBy(quest => quest.Id).ToList()) {
            if (AllianceMissions.CompleteAll(quest.Metadata.Basic, quest.State, alliance) && CanComplete(quest) && Complete(quest)) {
                completed++;
            }
        }
        return completed;
    }

    // Quest sub-type 33 (UIAllianceQuestDialog::FUN_140bf8c30, unit_140bf60f0_uialliancequestdialog.cpp:654): a quest in
    // progress whose basic property allows it is completed without its conditions for completion tickets (item tag
    // FameCompletionTicket), as many as calcFameCompletionTicketCount gives for its rank. The tickets are taken only
    // when the quest was completed (a full bag refuses it unless quest items go by mail).
    public bool CompleteWithTickets(int questId) {
        if (!TryGetQuest(questId, out Quest? quest) || quest.State != QuestState.Started || !quest.Metadata.Basic.CompletionTicket) {
            return false;
        }

        QuestMetadataBasic basic = quest.Metadata.Basic;
        int cost = Lua.CalcFameCompletionTicketCount((int) basic.Alliance, basic.Rank);
        if (cost <= 0) {
            return false;
        }

        lock (session.Item) {
            if (session.Item.Inventory.Find(ItemTag.FameCompletionTicket).Sum(item => item.Amount) < cost) {
                return false;
            }
            if (!Complete(quest, bypassConditions: true)) {
                return false;
            }
            if (!session.Item.Inventory.Consume([new IngredientInfo(ItemTag.FameCompletionTicket, cost)])) {
                logger.Error("Quest {QuestId} completed but {Count} completion tickets could not be taken", questId, cost);
            }
        }
        return true;
    }

    private List<QuestMetadata> Missions() {
        if (missionMetadata == null) {
            missionMetadata = session.QuestMetadata.GetQuestsByType(QuestType.AllianceQuest).Where(AllianceMissions.IsMission).ToList();
            missionWeekday = missionMetadata.Where(metadata => metadata.Basic.Repeat == 3)
                .Select(metadata => metadata.Basic.ResetWeekday)
                .FirstOrDefault(weekday => weekday != 0);
        }
        return missionMetadata;
    }

    // The reset weekday of the weekly missions (all of them have the same, usePeriod "fri"); 0 none, taken as sunday
    // (AllianceMissions.PeriodStart).
    private int MissionWeekday() {
        Missions();
        return missionWeekday;
    }

    // The held missions of the repeat the reset drops that were started before the period and are not completed.
    private void DropMissions(int repeat, DateTime start) {
        foreach (Quest quest in characterValues.Values.Concat(accountValues.Values).ToList()) {
            if (quest.Metadata.Basic.Repeat == repeat && AllianceMissions.Dropped(quest.Metadata, quest.State, quest.StartTime, start)) {
                Remove(quest);
            }
        }
    }

    // At login, before the quests are sent: the held missions whose period ended are dropped, as the daily reset
    // would have done.
    private void DropEndedMissions(DateTime now) {
        DropMissions(2, AllianceMissions.PeriodStart(2, 0, now));
        DropMissions(3, AllianceMissions.PeriodStart(3, MissionWeekday(), now));
    }

    /// <summary>
    /// The daily quest resets (QuestDailyReset): the held daily missions, daily field quests and wedding missions are
    /// dropped as the client drops them on Quest 0x19, 0x1E and 0x26, then those are sent. now is the start of the day
    /// the reset begins (AllianceMissions.ResetPeriodStart). A reset that fires before that midnight (timer drift, a
    /// clock change) waits for it on the session's queue: dropped at once, a daily completed earlier that day could be
    /// taken and rewarded again before midnight. A player who logs out before then gets the login check instead.
    /// </summary>
    public void ResetDailyQuests(DateTime now) {
        DateTime day = AllianceMissions.ResetPeriodStart(2, 0, now);
        if (now < day) {
            session.Scheduler.Schedule(() => ResetDailyQuests(day), day - now);
            return;
        }
        DropDailyQuests(day, true);
        session.Send(QuestPacket.DailyMissionReset(new DateTimeOffset(day).ToUnixTimeSeconds()));
        session.Send(QuestPacket.FieldQuestReset());
        session.Send(QuestPacket.WeddingMissionReset());
    }

    // atReset: the reset (every row of the three kinds, mission points by their Friday); else the login check, before
    // the quests are sent (the rows a missed reset would have dropped).
    private void DropDailyQuests(DateTime now, bool atReset) {
        int missions = 0, fieldQuests = 0, weddingMissions = 0;
        foreach (Quest quest in characterValues.Values.Concat(accountValues.Values).ToList()) {
            if (!QuestDailyReset.Dropped(quest.Metadata.Basic, quest.StartTime, now, atReset) || !Remove(quest)) {
                continue;
            }
            switch (QuestDailyReset.KindOf(quest.Metadata.Basic)) {
                case QuestDailyReset.Kind.DailyMission:
                    missions++;
                    break;
                case QuestDailyReset.Kind.FieldQuest:
                    fieldQuests++;
                    break;
                case QuestDailyReset.Kind.WeddingMission:
                    weddingMissions++;
                    break;
            }
        }
        if (!atReset && missions + fieldQuests + weddingMissions == 0) {
            return;
        }
        logger.Information("Daily quest {Reset} of {CharacterId}: dropped {Missions} daily missions, {FieldQuests} field quests, {WeddingMissions} wedding missions",
            atReset ? "reset" : "login check", session.CharacterId, missions, fieldQuests, weddingMissions);
    }

    /// <summary>
    /// Gets available quests that the npc can give or can be completed.
    /// </summary>
    public SortedDictionary<int, QuestMetadata> GetAvailableQuests(int npcId) {
        var results = new SortedDictionary<int, QuestMetadata>();
        IEnumerable<QuestMetadata> allQuests = session.QuestMetadata.GetQuestsByNpc(npcId);

        // Get any new quests that can be started
        foreach (QuestMetadata metadata in allQuests) {
            if (TryGetQuest(metadata.Id, out Quest? quest) && quest.State == QuestState.Completed && !CanStartAgain(quest)) {
                continue;
            }

            if (!session.Quest.CanStart(metadata)) {
                continue;
            }

            if (!results.TryAdd(metadata.Id, metadata)) {
                // error
            }
        }

        // Get any quests that are in progress and npc is the completion npc
        foreach ((int id, Quest quest) in session.Quest.characterValues) {
            if (quest.Metadata.Basic.CompleteNpc == npcId && quest.State != QuestState.Completed && PlayedClientQuests.Known(quest.Metadata.Basic)) {
                if (!results.TryAdd(id, quest.Metadata)) {
                    // error
                }
            }
        }

        foreach ((int id, Quest quest) in session.Quest.accountValues) {
            if (quest.Metadata.Basic.CompleteNpc == npcId && quest.State != QuestState.Completed && PlayedClientQuests.Known(quest.Metadata.Basic)) {
                if (!results.TryAdd(id, quest.Metadata)) {
                    // error
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Quest 7: drops the named quests that can expire (QuestRules.CanExpire) and tells the client which.
    /// </summary>
    public void Expired(IList<int> questIds) {
        var expired = new List<int>();
        foreach (int questId in questIds) {
            if (TryGetQuest(questId, out Quest? quest) && QuestRules.CanExpire(quest.Metadata.Basic, quest.State) && Remove(quest)) {
                expired.Add(questId);
            }
        }
        if (expired.Count > 0) {
            session.Send(QuestPacket.Expired(expired));
        }
    }

    public bool Remove(Quest quest) {
        using GameStorage.Request db = session.GameStorage.Context();
        if (quest.Metadata.Basic.Account > 0) {
            return accountValues.Remove(quest.Id) && db.DeleteQuest(session.AccountId, quest.Id);
        }
        return characterValues.Remove(quest.Id) && db.DeleteQuest(session.CharacterId, quest.Id);
    }

    // A quest-only drop (constraintsQuest: a global drop set, or an individual drop row) is for a player with a started
    // quest that still needs the item on this map: an item_exist condition naming the item (and the map, when it names
    // maps) below its count. KR has 39 such global drop sets, all for item_exist quests (10001270 needs 15 of 30000043
    // from the monsters of map 2000136), and 432 individual drop rows.
    public bool NeedsItem(int itemId, int mapId) {
        return QuestRules.UpdateCandidates(characterValues.Values.Concat(accountValues.Values), ConditionType.item_exist)
            .Where(quest => quest.State == QuestState.Started)
            .SelectMany(quest => quest.Conditions.Values)
            .Any(condition => condition.Metadata is { Type: ConditionType.item_exist, Codes: not null }
                              && condition.Counter < condition.Metadata.Value
                              && condition.Metadata.Check(session, targetLong: mapId, codeLong: itemId));
    }

    // The held count of an item changed (InventoryManager: added, removed or consumed): each started quest's
    // item_exist condition for the item takes the count the player holds now, up or down (QuestRules.HeldItem), and
    // gets an update when it moved. A drop or sale below the count takes a met condition back, as the client does.
    public void UpdateHeld(int itemId) {
        UpdateHeld(metadata => QuestRules.HeldItem(metadata) is var item && item != 0 && (itemId == 0 || item == itemId));
    }

    // The player's meso changed (CurrencyManager): meso_donation conditions take the meso the player has now, up or
    // down (QuestRules.HeldMeso).
    public void UpdateMeso() {
        UpdateHeld(QuestRules.HeldMeso);
    }

    // The worn items changed (EquipManager: equip or unequip): equip_exist conditions take the count of their items
    // worn now, up or down (QuestRules.EquippedCount).
    public void UpdateEquipped() {
        UpdateHeld(QuestRules.HeldEquip);
    }

    private void UpdateHeld(Func<ConditionMetadata, bool> match) {
        foreach (Quest quest in HeldCandidates(match)) {
            if (!CountHeld(quest, match)) {
                continue;
            }
            session.Send(QuestPacket.Update(quest));
            if ((quest.Metadata.Basic.Type == QuestType.FieldMission || StampQuests().Contains(quest.Id)) && CanComplete(quest)) {
                Complete(quest);
            }
        }
    }

    // Any held-count condition (QuestRules.IsHeldCount): the item, meso and worn-item counts the client keeps itself.
    private static bool AnyHeld(ConditionMetadata metadata) => QuestRules.IsHeldCount(metadata);

    // The started quests with a held-count condition the match takes.
    private List<Quest> HeldCandidates(Func<ConditionMetadata, bool> match) {
        return characterValues.Values.Concat(accountValues.Values)
            .Where(quest => quest.State == QuestState.Started && PlayedClientQuests.Known(quest.Metadata.Basic)
                            && quest.Conditions.Values.Any(condition => match(condition.Metadata)))
            .ToList();
    }

    // Sets the quest's held-count conditions the match takes to the count held now. True when one moved.
    private bool CountHeld(Quest quest, Func<ConditionMetadata, bool> match) {
        bool changed = false;
        foreach (Quest.Condition condition in quest.Conditions.Values) {
            if (!QuestRules.IsHeldCount(condition.Metadata) || !match(condition.Metadata)) {
                continue;
            }
            int counter = QuestRules.HeldCounter(condition.Metadata, HeldCount(condition.Metadata));
            if (counter != condition.Counter) {
                condition.Counter = counter;
                changed = true;
            }
        }
        return changed;
    }

    // What the client counts for a held-count condition (CQuestManager::UpdateQuestProgress): the held amount of the
    // item, the meso, or the worn listed items.
    private long HeldCount(ConditionMetadata metadata) {
        if (QuestRules.HeldMeso(metadata)) {
            return session.Currency.Meso;
        }
        if (QuestRules.HeldEquip(metadata)) {
            var equips = session.Item.Equips;
            return QuestRules.EquippedCount(metadata, equips.Gear.Values.Concat(equips.Outfit.Values).Concat(equips.Outfit2.Values)
                .Select(item => item.Id));
        }
        int item = QuestRules.HeldItem(metadata);
        return session.Item.Inventory.Find(item).Sum(held => (long) held.Amount);
    }

    public bool TryGetQuest(int questId, [NotNullWhen(true)] out Quest? quest) {
        return characterValues.TryGetValue(questId, out quest) || accountValues.TryGetValue(questId, out quest);
    }

    private void TryJobAdvance(int questId) {
        if (changeJobMetadata == null) {
            if (!session.TableMetadata.ChangeJobTable.Entries.TryGetValue(session.Player.Value.Character.Job, out ChangeJobMetadata? metadata)) {
                return;
            }
            changeJobMetadata = metadata;
        }

        if (changeJobMetadata.EndQuestId != questId) {
            return;
        }

        session.Player.Value.Character.Job = changeJobMetadata.ChangeJob;
        session.Config.Skill.SkillInfo.SetJob(changeJobMetadata.ChangeJob);
        session.Stats.Refresh();
        session.Field?.Broadcast(JobPacket.Advance(session.Player, session.Config.Skill.SkillInfo));
        // As the job script's confirm does: the job trophies count the new job and the other players' copy of the
        // character follows it. The row was the old job's.
        session.ConditionUpdate(ConditionType.job, codeLong: (int) changeJobMetadata.ChangeJob.Code());
        // subjob_change (90100000-11, "Change jobs to Saint Knight"): the code is the advanced job, changeJob.xml's
        // changeSubJobCode (101 for Knight II).
        session.ConditionUpdate(ConditionType.subjob_change, codeLong: (int) changeJobMetadata.ChangeJob);
        session.Player.Flag |= PlayerObjectFlag.Job;
        changeJobMetadata = null;
    }

    // The alliance's fame points, sent to the client (the alliance mission window's reputation).
    public void AddFamePoints(AllianceType alliance, int amount) {
        IDictionary<AllianceType, int> famePoints = session.Player.Value.Unlock.FamePoints;
        int points = AllianceMissions.AddFame(famePoints.GetValueOrDefault(alliance), amount);
        famePoints[alliance] = points;
        session.Send(UserEnvPacket.FamePoint(alliance, points));
        // The fame grade trophies (22500001 and on): code the alliance, target the grade. Every grade reached is
        // reported, so a gain that passes two grades counts both, and a character that held the fame before
        // fame_grade was counted gets its trophies at the next gain; a grade already counted moves nothing
        // (AchievementManager.Advances).
        foreach (int grade in FameLogRules.ReachedGrades(session.TableMetadata.FameTable, alliance, points)) {
            session.ConditionUpdate(ConditionType.fame_grade, targetLong: grade, codeLong: (int) alliance);
        }
        if (amount > 0 && FameLogRules.Roll(Random.Shared)) {
            AddFameLog(alliance, points, Random.Shared);
        }
    }

    // One fame log of the alliance's grade at these points that the character does not hold yet, sent to the client
    // (the fame board's history and its notice).
    public bool AddFameLog(AllianceType alliance, int points, Random random) {
        IDictionary<int, long> fameLogs = session.Player.Value.Unlock.FameLogs;
        FameTable table = session.TableMetadata.FameTable;
        int logId = FameLogRules.Pick(table, alliance, FameLogRules.Grade(table, alliance, points), fameLogs.Keys, random);
        if (logId == 0) {
            return false;
        }
        // The client keys its list by the time (uifamepointboarddialog.cpp), so no two logs share one.
        long time = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (fameLogs.Count > 0) {
            time = Math.Max(time, fameLogs.Values.Max() + 1);
        }
        fameLogs[logId] = time;
        session.Send(UserEnvPacket.FameLog(logId, time));
        return true;
    }

    private void CompleteChapter(int questId, bool ignoreItemRewards = false) {
        IEnumerable<ChapterBookTable.Entry> entries = session.TableMetadata.ChapterBookTable.Entries.Values.Where(entry => entry.EndQuestId == questId).ToList();
        if (!entries.Any()) {
            return;
        }

        foreach (ChapterBookTable.Entry entry in entries) {
            if (!TryGetQuest(entry.BeginQuestId, out Quest? quest) || quest.State != QuestState.Completed) {
                continue;
            }

            if (!ignoreItemRewards) {
                foreach (ItemComponent itemComponent in entry.Items) {
                    Item? item = session.Field?.ItemDrop.CreateItem(itemComponent.ItemId, itemComponent.Rarity, itemComponent.Amount);
                    if (item == null) {
                        continue;
                    }

                    if (!session.Item.Inventory.Add(item, true)) {
                        session.Item.MailItem(item);
                    }
                }
            }

            foreach (ChapterBookTable.Entry.SkillPoint point in entry.SkillPoints) {
                session.Config.AddSkillPoint(SkillPointSource.Chapter, point.Amount, point.Rank);
            }

            if (entry.StatPoints > 0) {
                session.Config.AddStatPoint(AttributePointSource.Quest, entry.StatPoints);
            }
        }
    }

    public void CompleteFieldMission(int mission) {
        if (!session.TableMetadata.FieldMissionTable.Entries.TryGetValue(mission, out FieldMissionTable.Entry? metadata)) {
            return;
        }

        int missionCompleteCount = characterValues.Values.Count(quest => quest.Metadata.Basic.Type == QuestType.FieldMission && quest.State == QuestState.Completed);
        missionCompleteCount += accountValues.Values.Count(quest => quest.Metadata.Basic.Type == QuestType.FieldMission && quest.State == QuestState.Completed);

        if (metadata.MissionCount > missionCompleteCount) {
            return;
        }

        session.Config.ExplorationProgress = metadata.MissionCount;
        session.Send(QuestPacket.UpdateExploration(session.Config.ExplorationProgress));

        if (metadata.Item != null) {
            Item? item = session.Field?.ItemDrop.CreateItem(metadata.Item.ItemId, metadata.Item.Rarity, metadata.Item.Amount);
            if (item != null) {
                if (!session.Item.Inventory.Add(item, true)) {
                    session.Item.MailItem(item);
                }
            }
        }

        if (metadata.StatPoints > 0) {
            session.Config.AddStatPoint(AttributePointSource.Exploration, metadata.StatPoints);
        }
    }

    public void LevelPotion(int level, int lastQuest = 0) {
        List<IGrouping<int, QuestMetadata>> chapterQuestsGroups = session.QuestMetadata.GetQuestsByType(QuestType.EpicQuest)
            .Where(q => q.Require.Level <= level)
            .Where(q => q.Basic.Disabled == false)
            .GroupBy(q => q.Basic.ChapterId)
            .ToList();

        using GameStorage.Request db = session.GameStorage.Context();
        bool lastQuestFound = false;
        foreach (IGrouping<int, QuestMetadata> chapterQuests in chapterQuestsGroups.OrderBy(q => q.Key)) {
            if (lastQuestFound) {
                break;
            }
            foreach (QuestMetadata metadata in chapterQuests.OrderBy(q => q.Id)) {
                if (TryGetQuest(metadata.Id, out Quest? quest)) {
                    if (quest.State == QuestState.Completed) {
                        continue;
                    }

                    foreach (Quest.Condition condition in quest.Conditions.Values) {
                        condition.Counter = (int) condition.Metadata.Value;
                    }
                    quest.State = QuestState.Completed;
                    quest.CompletionCount++;
                    quest.EndTime = DateTime.Now.ToEpochSeconds();
                    continue;
                }

                if (metadata.Require.Job.Length > 0 && !metadata.Require.Job.Contains(session.Player.Value.Character.Job.Code())) {
                    continue;
                }

                var epicQuest = new Quest(metadata) {
                    State = QuestState.Completed,
                    StartTime = DateTime.Now.ToEpochSeconds(),
                    EndTime = DateTime.Now.ToEpochSeconds() + 1,
                    CompletionCount = 1,
                    Track = true,
                };

                for (int i = 0; i < metadata.Conditions.Length; i++) {
                    epicQuest.Conditions.Add(i, new Quest.Condition(metadata.Conditions[i]));
                    epicQuest.Conditions[i].Counter = (int) epicQuest.Conditions[i].Metadata.Value;
                }

                long ownerId = metadata.Basic.Account > 0 ? session.AccountId : session.CharacterId;
                epicQuest = db.CreateQuest(ownerId, epicQuest);
                if (epicQuest == null) {
                    logger.Error("Failed to create quest entry {questId}", metadata.Id);
                    continue;
                }
                Add(epicQuest);
                TryJobAdvance(epicQuest.Id);
                CompleteChapter(epicQuest.Id, true);

                if (epicQuest.Id == lastQuest) {
                    lastQuestFound = true;
                    break;
                }
            }
        }

        //TODO: Start and load fame quests
        Load();
    }

    // Quest sub-type 18: the portal of a started quest, asked for again from the quest's NPC.
    public void ResummonPortal(int questId) {
        if (session.Field is null) return;
        TryGetQuest(questId, out Quest? quest);
        bool ownsPortal = session.Field.GetPortals().Any(portal => portal is FieldQuestPortal questPortal && questPortal.Owner == session.Player);
        if (!QuestRules.CanResummonPortal(quest?.State ?? QuestState.None, quest?.Metadata.SummonPortal != null,
                session.NpcScript?.Npc != null, ownsPortal)) {
            return;
        }
        SummonPortal(quest!);
    }

    private void SummonPortal(Quest quest) {
        if (session.Field is null) return;
        if (session.NpcScript?.Npc == null) {
            logger.Warning("Cannot summon quest portal for quest {QuestId}: No NPC script context", quest.Id);
            return;
        }

        FieldPortal portal = session.Field.SpawnPortal(quest.Metadata.SummonPortal!, session.NpcScript.Npc, session.Player);
        session.Send(PortalPacket.Add(portal));
        session.Send(QuestPacket.SummonPortal(session.NpcScript.Npc.ObjectId, portal.Value.Id, portal.StartTick));
    }

    /// <summary>
    /// Only used for debugging purposes. Completes all quests in the chapter silently.
    /// </summary>
    /// <param name="chapterId"></param>
    public void DebugCompleteChapter(int chapterId) {
        using GameStorage.Request db = session.GameStorage.Context();
        IEnumerable<int> questIds = session.QuestMetadata.GetQuestsByChapter(chapterId).Select(q => q.Id);
        foreach (int questId in questIds) {
            if (TryGetQuest(questId, out Quest? quest)) {
                if (quest.State == QuestState.Completed) {
                    continue;
                }

                quest.State = QuestState.Completed;
                quest.CompletionCount++;
                quest.EndTime = DateTime.Now.ToEpochSeconds();
            } else {
                if (!session.QuestMetadata.TryGet(questId, out QuestMetadata? metadata)) {
                    continue;
                }
                var newQuest = new Quest(metadata) {
                    State = QuestState.Completed,
                    StartTime = DateTime.Now.ToEpochSeconds(),
                    EndTime = DateTime.Now.ToEpochSeconds() + 1,
                    CompletionCount = 1,
                    Track = true,
                };

                for (int i = 0; i < newQuest.Metadata.Conditions.Length; i++) {
                    newQuest.Conditions.Add(i, new Quest.Condition(newQuest.Metadata.Conditions[i]));
                    newQuest.Conditions[i].Counter = (int) newQuest.Conditions[i].Metadata.Value;
                }

                long ownerId = newQuest.Metadata.Basic.Account > 0 ? session.AccountId : session.CharacterId;
                newQuest = db.CreateQuest(ownerId, newQuest);
                if (newQuest == null) {
                    logger.Error("Failed to create quest entry {questId}", metadata.Id);
                    continue;
                }
                Add(newQuest);
            }
        }

        Load();
    }

    public void Save(GameStorage.Request db) {
        db.SaveQuests(session.AccountId, accountValues.Values);
        db.SaveQuests(session.CharacterId, characterValues.Values);
    }
}
