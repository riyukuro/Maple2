using Maple2.Model.Enum;
using Maple2.Model.Metadata;

namespace Maple2.Server.Game.Util;

public static class QuestRules {
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
