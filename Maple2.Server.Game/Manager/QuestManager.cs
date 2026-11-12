using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Maple2.Database.Extensions;
using Maple2.Database.Storage;
using Maple2.Model;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
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
        Initialize(db);
    }

    public void Load() {
        DropEndedMissions(DateTime.Now);
        session.Send(QuestPacket.LoadExploration(session.Config.ExplorationProgress));

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

        Quest? quest;
        if (held != null) {
            // The stored row is reused: started again with fresh conditions, its completion count kept. The client
            // takes the Start packet as a new quest in place of the completed one.
            quest = held;
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

        // TODO: Confirm inventory can hold all the items.
        foreach (QuestMetadataReward.Item acceptReward in metadata.AcceptReward.EssentialItem) {
            Item? reward = session.Field?.ItemDrop.CreateItem(acceptReward.Id, acceptReward.Rarity, acceptReward.Amount);
            if (reward == null) {
                logger.Error("Failed to create quest reward {RewardId}", acceptReward.Id);
                continue;
            }
            if (!session.Item.Inventory.Add(reward, true)) {
                logger.Error("Failed to add quest reward {RewardId} to inventory", acceptReward.Id);
            }
        }

        session.ConditionUpdate(ConditionType.quest_accept, codeLong: quest.Id);
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

        return require.SelectableQuest.Length <= 0 || SelectableQuests(require.SelectableQuest);
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

        // The exp the reward line shows: the fixed exp or the relativeExp curve at the player's level, whichever is
        // larger, with the rank factor of field and alliance quests.
        QuestMetadataBasic basic = quest.Metadata.Basic;
        long exp = QuestExpRules.Exp(reward.Exp, session.Exp.TypeValue(reward.RelativeExp), basic.Type, basic.Rank, (int) basic.Alliance);
        if (guildFactor != null) {
            exp = GuildRules.QuestRewardBonus(exp, guildFactor.UserExp);
        }
        if (exp > 0) {
            session.Exp.AddExp(exp);
        }

        if (reward.Meso > 0) {
            session.Currency.Meso += reward.Meso;
        }

        if (reward.Treva > 0) {
            session.Currency[CurrencyType.Treva] += reward.Treva;
        }

        if (reward.Rue > 0) {
            session.Currency[CurrencyType.Rue] += reward.Rue;
        }

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
        if (QuestRules.CountsAsAllianceFieldMission(quest.Metadata.Basic)) {
            session.ConditionUpdate(ConditionType.quest_field, codeLong: (long) quest.Metadata.Basic.Alliance);
        }

        quest.EndTime = DateTime.Now.ToEpochSeconds();
        quest.State = QuestState.Completed;
        quest.CompletionCount++;
        session.Send(QuestPacket.Complete(quest));
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
    }

    // The alliance's fame points, sent to the client (the alliance mission window's reputation).
    public void AddFamePoints(AllianceType alliance, int amount) {
        IDictionary<AllianceType, int> famePoints = session.Player.Value.Unlock.FamePoints;
        int points = AllianceMissions.AddFame(famePoints.GetValueOrDefault(alliance), amount);
        famePoints[alliance] = points;
        session.Send(UserEnvPacket.FamePoint(alliance, points));
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
            Item? item = session.Field?.ItemDrop.CreateItem(metadata.Item.ItemId, metadata.Item.Rarity, metadata.Item.Rarity);
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
