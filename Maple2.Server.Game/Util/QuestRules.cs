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

    // Quest 7 (Expired): the client sends it for a quest in progress whose end time has come, and a quest has an end
    // time only with a reset weekday, a duration or an event tag (FUN_14074b090, CQuestManager::FUN_1416d3010,
    // questmanager_2.cpp:2157). Anything else, a completed quest above all, stays: its row is what keeps a repeat
    // quest from being started and rewarded again in the same period.
    public static bool CanExpire(QuestMetadataBasic basic, QuestState state) {
        return state == QuestState.Started
               && (basic.ResetWeekday != 0 || basic.DurationMinutes != 0 || !string.IsNullOrEmpty(basic.EventTag));
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
}
