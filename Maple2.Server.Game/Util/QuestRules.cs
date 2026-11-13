using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;

namespace Maple2.Server.Game.Util;

public static class QuestRules {
    // A completed quest that may be started again, the client's own rule (CQuestManagerCommon::vfunc_o10_21,
    // commonfiles/questdata.cpp:376): repeat 0 never; 2 (daily) not on the calendar day the quest was last
    // started; 3 (weekly) not before the start of the reset weekday after the last start (NextWeekdayStart,
    // timeutil.cpp:403); any other repeat at once. Times are local, as the client's CMsTime.
    public static bool CanStartAgain(int repeat, int resetWeekday, long startTime, DateTime now) {
        DateTime start = DateTimeOffset.FromUnixTimeSeconds(startTime).LocalDateTime;
        return repeat switch {
            0 => false,
            2 => start.Date != now.Date,
            3 => now >= NextWeekdayStart(resetWeekday, start),
            _ => true,
        };
    }

    // A completed quest counts for quest_daily when it is a daily quest (repeat 2): every quest the quest_daily rows
    // list is one, and the row's codes pick which.
    public static bool CountsAsDaily(QuestMetadataBasic basic) => basic.Repeat == 2;

    // A completed quest counts for quest_guide when it is a daily mission (type 3, the navigator's guide missions):
    // the quest_guide rows have no codes and read "complete a daily mission" (74000017, achievement 22300047).
    public static bool CountsAsGuide(QuestMetadataBasic basic) => basic.Type == QuestType.DailyMission;

    // A completed alliance quest (type 9) counts for quest_alliance with its alliance as the code: the rows read
    // "complete a Maple Alliance mission" with code 2 (the drill 93000188), and the code is the alliance id (1 to 8).
    public static bool CountsAsAllianceMission(QuestMetadataBasic basic) =>
        basic.Type == QuestType.AllianceQuest && basic.Alliance != AllianceType.AnyFaction;

    // A completed alliance quest of a rank also counts for quest_alliance_by_grade, with the rank's letter as the code
    // and the alliance as the target: the rows read "complete N D Rank missions" with code D and targets 1 to 5
    // (22500034), Kritias 6 to 8 (22501017) and Georg 9 (22501045). The quest XML gives the rank as that letter and
    // the ingest stores its index (S 0, A 1, B 2, C 3, D 4, none 5); each trophy counts its own rank only.
    public static string? AllianceRankLetter(QuestMetadataBasic basic) {
        if (basic.Type != QuestType.AllianceQuest || basic.Alliance == AllianceType.AnyFaction) {
            return null;
        }
        return basic.Rank switch {
            0 => "S",
            1 => "A",
            2 => "B",
            3 => "C",
            4 => "D",
            _ => null,
        };
    }

    // A completed field quest (type 8) of an alliance counts for quest_field with its alliance as the code: the rows
    // read "complete a Maple Alliance field mission" with code 2 (93000159).
    public static bool CountsAsAllianceFieldMission(QuestMetadataBasic basic) =>
        basic.Type == QuestType.FieldQuest && basic.Alliance != AllianceType.AnyFaction;

    // Quest 7 (Expired): the client sends it for a quest in progress whose end time has come, and a quest has an end
    // time only with a reset weekday, a duration or an event tag (FUN_14074b090, CQuestManager::FUN_1416d3010,
    // questmanager_2.cpp:2157). Anything else, a completed quest above all, stays: its row is what keeps a repeat
    // quest from being started and rewarded again in the same period.
    public static bool CanExpire(QuestMetadataBasic basic, QuestState state) {
        return state == QuestState.Started
               && (basic.ResetWeekday != 0 || basic.DurationMinutes != 0 || !string.IsNullOrEmpty(basic.EventTag));
    }

    // The first state range of a quest's talk script at an npc: 100 the accept talk (a quest not held, or a completed
    // one that can be started again), 200 the talk of a started quest, 300 the complete talk, only at the quest's
    // complete npc once every condition is met (KMS2 sends a 200 state with the close button for a quest still in
    // progress); 0 none.
    public static int TalkStateBase(QuestState? held, bool canStartAgain, bool conditionsMet, bool atCompleteNpc) {
        return held switch {
            null or QuestState.None => 100,
            QuestState.Started => conditionsMet && atCompleteNpc ? 300 : 200,
            QuestState.Completed => canStartAgain ? 100 : 0,
            _ => 0,
        };
    }

    // Quest 4 (Complete) names the talk's npc, or none from the popup talk. With an npc: it is on the field and is the
    // quest's complete npc. Without one: the quest is completed remotely or has no complete npc (KMS2: 8 remote
    // quests and 2 without an npc among the 10 requests with none). A remote completion that names a field
    // (remoteComplete requireField, 22 quests) is taken on that field only: the client offers it only in exactly that
    // field (CStoryQuest::FUN_14180fc80, client/ms2/stringtable.cpp:334-338).
    public static bool CanCompleteAt(QuestMetadata metadata, bool named, int npcId, int mapId) {
        if (named) {
            return npcId != 0 && (metadata.Basic.CompleteNpc == 0 || metadata.Basic.CompleteNpc == npcId);
        }
        if (metadata.RemoteComplete.Type != QuestRemoteType.None) {
            return metadata.RemoteComplete.MapId == 0 || metadata.RemoteComplete.MapId == mapId;
        }
        return metadata.Basic.CompleteNpc == 0;
    }

    // CQuestCommon::GetEndTime (questdata.cpp:253), in epoch seconds: a reset weekday gives the start of that weekday
    // after the start, else a duration the start plus its minutes (a quest not started counts from now); an event tag
    // gives the end of its term (the QuestTag event), 0 when the term is gone or the quest was started outside it;
    // with both, the earlier one.
    public static long EndTime(QuestMetadataBasic basic, long startTime, long now, (long Start, long End)? term) {
        long period = 0;
        long start = startTime != 0 ? startTime : now;
        if (basic.ResetWeekday != 0) {
            DateTime local = DateTimeOffset.FromUnixTimeSeconds(start).LocalDateTime;
            period = new DateTimeOffset(NextWeekdayStart(basic.ResetWeekday, local)).ToUnixTimeSeconds();
        } else if (basic.DurationMinutes != 0) {
            period = start + basic.DurationMinutes * 60L;
        }
        long eventEnd = 0;
        if (!string.IsNullOrEmpty(basic.EventTag)) {
            if (term is not { } t) {
                return 0;
            }
            if (startTime != 0 && !(t.Start <= startTime && startTime < t.End)) {
                return 0;
            }
            eventEnd = t.End;
        }
        if (period != 0 && eventEnd != 0) {
            return Math.Min(period, eventEnd);
        }
        return period != 0 ? period : eventEnd;
    }

    // CQuestCommon::IsExpired (questdata.cpp:241): a quest with an end time (CanExpire's fields) whose end has come.
    // The shared completion check (FUN_140746ba0, questdata.cpp:653) refuses such a quest with invalid_date (4)
    // before it looks at the state or the conditions.
    public static bool IsExpired(QuestMetadataBasic basic, long startTime, long now, (long Start, long End)? term) {
        if (basic.ResetWeekday == 0 && basic.DurationMinutes == 0 && string.IsNullOrEmpty(basic.EventTag)) {
            return false;
        }
        return now >= EndTime(basic, startTime, now, term);
    }

    // Quest 2 (Abandon): a forfeitable quest known to the played client that is not completed, for the same reason.
    public static bool CanForfeit(QuestMetadataBasic basic, QuestState state) {
        return basic.Forfeitable && state != QuestState.Completed && PlayedClientQuests.Known(basic);
    }

    // NextWeekdayStart: the start of the next day with this weekday (1 sun .. 7 sat) after the day of the time; the
    // same weekday gives a week later.
    public static DateTime NextWeekdayStart(int weekday, DateTime time) {
        int dayOfWeek = (int) time.DayOfWeek + 1;
        int delta = dayOfWeek < weekday ? weekday - dayOfWeek : 7 - (dayOfWeek - weekday);
        return time.Date.AddDays(delta);
    }

    // The stored quests a condition of this type can move on: not completed, with such a condition, and known to
    // the played client (PlayedClientQuests). The client would get an update for a quest it never loaded otherwise.
    public static IEnumerable<Quest> UpdateCandidates(IEnumerable<Quest> quests, ConditionType type) =>
        quests.Where(quest => quest.State != QuestState.Completed && PlayedClientQuests.Known(quest.Metadata.Basic)
                              && quest.Conditions.Values.Any(condition => condition.Metadata.Type == type));

    // item_exist (and item_inven, which KR does not use): the client's progress is the count of the item the player
    // holds (CQuestManager::UpdateQuestProgress counts the item's inventory tab, the first code only), not a count
    // of pickups; it keeps no progress the server sends for them. The item counted, or 0 for other conditions.
    public static int HeldItem(ConditionMetadata metadata) =>
        metadata is { Type: ConditionType.item_exist or ConditionType.item_inven, Codes.Integers: [var item, ..] } ? item : 0;

    // meso_donation (the funding quests 93000073, 93000139-93000142): the client's progress is the meso the player
    // has (CQuestManager::UpdateQuestProgress case 0x49 reads the character's meso), up or down.
    public static bool HeldMeso(ConditionMetadata metadata) => metadata.Type == ConditionType.meso_donation;

    // equip_exist (the event quests 95000000-95000008): the client's progress is the count of distinct listed item
    // ids among the worn items (CQuestManager::FUN_1416d3af0 over CEquipment::CollectShownItems, which takes each
    // worn item once, from the first slot it fills).
    public static bool HeldEquip(ConditionMetadata metadata) =>
        metadata is { Type: ConditionType.equip_exist, Codes.Integers.Length: > 0 };

    // The conditions whose progress the client counts itself from what the player has, not from events.
    public static bool IsHeldCount(ConditionMetadata metadata) => HeldItem(metadata) != 0 || HeldMeso(metadata) || HeldEquip(metadata);

    // The distinct listed item ids among the worn item ids (0 for a condition without codes). The client also skips
    // a worn item whose item-stream flag after the glamour forge count is set (CItem::FUN_140529620, meaning not
    // known); the server always writes it false, so every worn listed item counts.
    public static int EquippedCount(ConditionMetadata metadata, IEnumerable<int> wornItemIds) {
        if (metadata.Codes?.Integers is not { Length: > 0 } listed) {
            return 0;
        }
        return wornItemIds.Where(listed.Contains).Distinct().Count();
    }

    // The counter for a held count: the count up to the condition's value.
    public static int HeldCounter(ConditionMetadata metadata, long held) => (int) Math.Clamp(held, 0, metadata.Value);

    // A level condition moves only on a level-up (ExperienceManager.LevelUp sends the new level), so a quest started
    // at or above its level target waited for the next level-up and never came due at the top level. The start counts
    // the current level once the same way (ConditionUtil level: level >= a target value). Conditions with codes or a
    // party count are left to the level-up. True when a counter moved.
    public static bool CountLevelAtStart(IEnumerable<Quest.Condition> conditions, int level) {
        bool changed = false;
        foreach (Quest.Condition condition in conditions) {
            ConditionMetadata metadata = condition.Metadata;
            if (metadata.Type != ConditionType.level || condition.Counter >= metadata.Value) {
                continue;
            }
            if (metadata.Codes != null || metadata.PartyCount > 0 || metadata.GuildPartyCount > 0) {
                continue;
            }
            if (metadata.Target?.Integers is not { } targets || !targets.Any(value => level >= value)) {
                continue;
            }

            condition.Counter = (int) Math.Min(metadata.Value, condition.Counter + 1L);
            changed = true;
        }

        return changed;
    }

    // Quest 0x18 (remote complete): the claim button of the daily mission page and the level guide
    // (uidailymissiondialog.cpp:321, uilevelupguidedialog.cpp:380). The client sends it only for a quest it holds as
    // started with every counter at its goal, so a refusal means the two disagree; the server says why in its log.
    public enum RemoteClaim {
        Done,
        NoMetadata,
        NotHeld,
        Completed,
        NotInClient,
        Unmet,
        InventoryFull,
    }

    public static RemoteClaim RemoteClaimCheck(bool hasMetadata, Quest? held, bool knownToClient, bool conditionsMet) {
        if (held == null) {
            return hasMetadata ? RemoteClaim.NotHeld : RemoteClaim.NoMetadata;
        }
        if (held.State == QuestState.Completed) {
            return RemoteClaim.Completed;
        }
        if (!knownToClient) {
            return RemoteClaim.NotInClient;
        }
        return conditionsMet ? RemoteClaim.Done : RemoteClaim.Unmet;
    }

    // The answer of a refused claim: Quest mode 0 with error 2, "failed to complete the quest" (string 0x5ef,
    // CQuestManager::ShowQuestErrorMessage, questmanager_2.cpp:1187). A full inventory has its own answer already
    // (ItemInventory s_err_inventory from QuestManager.Complete), and a claim of a quest just completed is a second
    // click before the Complete packet came, which needs no box.
    public static bool AnswersRefusal(RemoteClaim claim) =>
        claim is not (RemoteClaim.Done or RemoteClaim.InventoryFull or RemoteClaim.Completed);

    // The quests the client asks to start by itself (Quest, sub-type 8) when it enters a field: a quest that is
    // not an exploration quest, starts by itself and is not locked, or an exploration quest whose first
    // progress map is the field the player is in.
    public static bool ClientStartsItself(QuestType type, bool autoStart, bool locked, int[]? progressMaps, int mapId) {
        if (type == QuestType.FieldMission) {
            return progressMaps is { Length: > 0 } && progressMaps[0] == mapId;
        }

        return autoStart && !locked;
    }

    // Quest sub-type 18 (the client sends it when the talk of a quest in progress ends and the quest has a
    // summonPortal): the portal is made again only for a started quest that has one, while the player is in
    // a talk with an NPC, and while no quest portal of theirs is still up in the field.
    public static bool CanResummonPortal(QuestState state, bool hasSummonPortal, bool inNpcTalk, bool ownsLivePortal) {
        return state == QuestState.Started && hasSummonPortal && inNpcTalk && !ownsLivePortal;
    }

    // Quest sub-type 19 (CQuestCommon::FUN_140744670, questdata.cpp:221, is the client's own gate): the
    // player goes back to the hidden field of a quest only when the quest is started, its data has
    // useReEntryHiddenMap, its first progress map is set and is not the field the player is in, and the
    // player is alive.
    public static bool CanReEnterHiddenMap(QuestState state, bool useReEntryHiddenMap, int[]? progressMaps, int mapId, bool isDead) {
        return state == QuestState.Started && useReEntryHiddenMap && !isDead
               && progressMaps is { Length: > 0 } && progressMaps[0] != 0 && progressMaps[0] != mapId;
    }

    // What a user_find condition (type 286) tests about the player that was found. The client fills the same
    // values in CQuest::CheckUserFind (questmanager_1.cpp:169): job group, level, gender of both players,
    // title, ride and the ids of the shown equipment.
    public readonly record struct FoundUser(int JobCode, int Level, int Gender, int FinderGender, int TitleId, int RideId, IReadOnlyCollection<int> EquipItemIds);

    // The target of a user_find condition is a dictionary, stored as the comma-split pieces of
    // "dict:{key:value,key:value}". Null when the pieces are not such a dictionary.
    public static Dictionary<string, string>? ParseDict(string[]? strings) {
        if (strings is not { Length: > 0 }) {
            return null;
        }

        string text = string.Join(",", strings).Trim();
        if (!text.StartsWith("dict:{") || !text.EndsWith('}')) {
            return null;
        }

        var result = new Dictionary<string, string>();
        foreach (string piece in text["dict:{".Length..^1].Split(',', StringSplitOptions.RemoveEmptyEntries)) {
            int colon = piece.IndexOf(':');
            if (colon <= 0) {
                return null;
            }
            result[piece[..colon].Trim()] = piece[(colon + 1)..].Trim();
        }
        return result;
    }

    // CQuestConditionCheckerUserFind::Check (conditionutil.cpp:1003): every key of the dictionary that has a
    // test must pass; a key without a test is skipped; no dictionary fails.
    public static bool MatchesUserFind(string[]? target, in FoundUser user) {
        Dictionary<string, string>? dict = ParseDict(target);
        if (dict == null) {
            return false;
        }

        foreach ((string key, string value) in dict) {
            int[] numbers = value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => int.TryParse(part, out int number) ? number : 0).ToArray();
            int first = numbers.Length > 0 ? numbers[0] : 0;
            IReadOnlyCollection<int> equips = user.EquipItemIds;
            bool passed = key switch {
                "jobId" => first == user.JobCode,
                "level" => numbers.Length == 2 && user.Level >= numbers[0] && user.Level <= numbers[1],
                "gender" => first == 2 || first == user.Gender,
                "equipItemIds" or "equipItemIdsAnd" => numbers.All(equips.Contains),
                "equipItemIdsOr" => numbers.Any(equips.Contains),
                "titleId" => first == user.TitleId,
                "rideeId" => first == user.RideId,
                "oppositeGender" => (first != 0 || value.Equals("true", StringComparison.OrdinalIgnoreCase)) == (user.FinderGender != user.Gender),
                _ => true,
            };
            if (!passed) {
                return false;
            }
        }
        return true;
    }

    // Quest sub-type 28 (QuestUserFindAction, actionkeyobject.cpp:3010 and CQuestManager::FindNearestQuestTarget,
    // questmanager_2.cpp:2010): the client offers the action only outside a PvP field, for another player
    // who is alive and within the InteractionDistance constant. A distance constant of 0 or less (not
    // loaded) tests nothing; slack is the server's room for position lag.
    public static bool CanFindUser(bool pvpField, bool self, bool targetDead, float distance, float reach, float slack) {
        if (pvpField || self || targetDead) {
            return false;
        }
        return reach <= 0 || distance <= reach + slack;
    }

    public static bool ClientStartsItself(QuestMetadata metadata, int mapId) {
        QuestMetadataBasic basic = metadata.Basic;
        return ClientStartsItself(basic.Type, basic.AutoStart, basic.Disabled, basic.ProgressMaps, mapId);
    }

    // The quest window's "go to" button (CQuestCommon::FUN_1407441f0, commonfiles/pvpinfo.cpp; the client sends Quest
    // mode 12 only when it holds): gotoNpc is enabled with a gotoField, the quest is started and every condition is
    // met (CQuest::IsComplete), and the character stands neither in the gotoField nor in a field of the quest's
    // complete npc (fieldmetadata.xml <npc>).
    public static bool CanGoToNpc(QuestMetadataGoToNpc goTo, QuestState state, bool conditionsMet, IReadOnlyList<int>? npcFields, int mapId) {
        if (!goTo.Enabled || goTo.MapId == 0 || state != QuestState.Started || !conditionsMet) {
            return false;
        }
        return mapId != goTo.MapId && npcFields?.Contains(mapId) != true;
    }
}
