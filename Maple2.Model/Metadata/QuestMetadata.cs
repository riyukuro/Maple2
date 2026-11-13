using Maple2.Model.Enum;

namespace Maple2.Model.Metadata;

public record QuestMetadata(
    int Id,
    string? Name,
    QuestMetadataBasic Basic,
    QuestMetadataRequire Require,
    QuestMetadataReward AcceptReward,
    QuestMetadataReward CompleteReward,
    QuestRemoteAccept RemoteAccept,
    QuestRemoteComplete RemoteComplete,
    QuestMetadataGoToNpc GoToNpc,
    QuestMetadataGoToDungeon GoToDungeon,
    QuestDispatch? Dispatch,
    QuestMentoringMission? Mentoring,
    QuestSummonPortal? SummonPortal,
    QuestEventMissionType EventMissionType,
    ConditionMetadata[] Conditions) : ISearchResult;

public record QuestMetadataBasic(
    int ChapterId,
    QuestType Type,
    int Account, // 1 and 2 are both account quests. Unsure of the difference.
    int StandardLevel,
    bool Forfeitable,
    string EventTag,
    bool AutoStart,
    bool Disabled,
    bool UsePostbox, // quest is sent to player remotely
    int StartNpc,
    int CompleteNpc,
    int[]? CompleteMaps,
    int[]? ProgressMaps,
    bool UseReEntryHiddenMap = false, // the quest's hidden field can be entered again (Quest sub-type 19)
                                      // With a played client: its quest table has no such quest (PlayedClientQuests), so the server neither starts,
                                      // offers nor sends it.
    bool NotInPlayedClient = false,
    // basic repeatable: 0 the quest is done once, 2 again on another day, 3 again from the week's reset day; any
    // other value again at once (CQuestManagerCommon::vfunc_o10_21, questdata.cpp:376).
    int Repeat = 0,
    // basic usePeriod as a weekday, 1 (sun) to 7 (sat), the reset day of a weekly quest; 0 none (FUN_14028e160).
    int ResetWeekday = 0,
    // basic usePeriod as minutes when it is not a weekday (atoi, FUN_140718170): the quest ends that long after its
    // start; 0 none (CQuestCommon::GetEndTime, questdata.cpp:253).
    int DurationMinutes = 0,
    // basic alliance as the client's id (Parse_14021a330: darkwind 1 .. georg 9, 0 none) and basic rank (Parse_14021a570:
    // S 0, A 1, B 2, C 3, D 4, anything else 5). The alliance window lists the alliance quests (type 9) of its alliance.
    AllianceType Alliance = AllianceType.AnyFaction,
    int Rank = 5,
    // basic property holds AvailableCompletionTicket (ParseProperty, questdata.cpp:2801, bit 2): the quest in progress
    // may be completed with completion tickets (Quest sub-type 33). The client also asks its feature
    // FameCompletionTicket (UIAllianceQuestDialog::CanUseCompletionTicket), in its own region's column, which need not
    // be the server's LOCALE; the server leaves that test to the client.
    bool CompletionTicket = false,
    // guide guideType in the client's numbering (CQuestData m_338, Parse_140217a00): 1 level, 2 level_boost,
    // 3 daily_pick_once, 4 daily_pick_repeat, 5 mission_point; 0 none. The daily reset drops daily missions by it
    // (QuestDailyReset).
    int GuideType = 0);

public record QuestMetadataRequire(
    short Level,
    short MaxLevel,
    JobCode[] Job,
    int[] Quest,
    int[] SelectableQuest,
    int Achievement,
    (int, int) UnrequiredAchievement, // (AchievementId, Grade) Player CANNOT have this achievement to start the quest.
    int GearScore,
    // require groupID: only the group's picked quests of the period are accepted (QuestGroupPick); a weekly alliance
    // quest with a group is not one of the week's listed missions (CQuestManager::vfunc_1).
    int GroupId = 0,
    // require alliance and fameGrade: the alliance's fame grade must reach it; masteryType and masteryGrade: the
    // mastery's grade must reach it (CQuestManagerCommon::CanAcceptQuest, questdata.cpp:576). 0 checks nothing.
    AllianceType FameAlliance = AllianceType.AnyFaction,
    int FameGrade = 0,
    MasteryType MasteryType = MasteryType.Unknown,
    int MasteryGrade = 0,
    // require unrequireGroup: the quest is not accepted while a picked quest of one of these groups is in progress,
    // or completed and not yet repeatable (CQuestManagerCommon::CanAcceptQuest, questdata.cpp:520).
    int[]? UnrequireGroup = null,
    // require unrequire: refused while one of these quests is in progress or completed; progressQuest: every one of
    // these must be in progress (CanAcceptQuest, questdata.cpp:501-517).
    int[]? Unrequire = null,
    int[]? ProgressQuest = null,
    // require guildNpcLevel: the player's guild must have its quest npc at exactly this level, so no guild refuses
    // (CanAcceptQuest, questdata.cpp:569). 0 checks nothing.
    int GuildNpcLevel = 0,
    // require dayOfWeek: the local weekday, 1 Sunday to 7 Saturday, must be listed (vfunc_o10_21, questdata.cpp:342).
    int[]? Weekdays = null,
    // require dungeon: refused until the player has cleared this room (the record's clear count, questdata.cpp:588);
    // unrequireDungeon: refused once it has a clear (:590); enterDungeon: refused while the player's enter state for
    // the room is 10 or above (:592); dungeonBeginner: refused unless at least one listed room is one the player cannot
    // claim the helper reward of (:594). 0 or no list checks nothing.
    int Dungeon = 0,
    int UnrequireDungeon = 0,
    int EnterDungeon = 0,
    int[]? DungeonBeginner = null);

public record QuestMetadataReward(
    int Meso,
    int Exp,
    ExpType RelativeExp,
    int GuildFund,
    int GuildExp,
    int GuildCoin,
    int MenteeCoin,
    int MissionPoint,
    int Treva,
    int Rue,
    List<QuestMetadataReward.Item> EssentialItem,
    List<QuestMetadataReward.Item> EssentialJobItem,
    // useMainFamePoint: when set, the reward gives the quest's alliance fame points (Lua calcQuestRewardFamePoint).
    int UseMainFamePoint = 0,
    // relativeMeso: the reward contents row (rewardcontent.xml rewardID) whose meso the reward gives instead of the
    // fixed money; 0 for none. The guild quests' GuildQuest (1003) and GuildWeeklyQuest (1004).
    int RelativeMeso = 0,
    // weddingExp: the intimacy the wedding missions add to the player's marriage (questdata.cpp:1422; the mission
    // page shows it, uiweddingpage.cpp:414); 0 for none.
    long WeddingExp = 0) {

    public record Item(int Id, int Rarity, int Amount);
}

public record QuestMetadataGoToNpc(
    bool Enabled,
    int MapId,
    int PortalId);

public record QuestMetadataGoToDungeon(
    QuestState State,
    int MapId,
    int InstanceId);

public record QuestRemoteAccept(
    QuestRemoteType Type,
    int MapId);

public record QuestRemoteComplete(
    QuestRemoteType Type,
    int MapId,
    bool RequireDungeonClear);

public record QuestDispatch(
    QuestDispatchType Type,
    int MapId,
    int PortalId,
    int Script);

public record QuestMentoringMission(
    int OpeningDay,
    int Season);

public record QuestSummonPortal(
    int MapId,
    int PortalId);

