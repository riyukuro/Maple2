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

    public static bool ClientStartsItself(QuestMetadata metadata, int mapId) {
        QuestMetadataBasic basic = metadata.Basic;
        return ClientStartsItself(basic.Type, basic.AutoStart, basic.Disabled, basic.ProgressMaps, mapId);
    }
}
