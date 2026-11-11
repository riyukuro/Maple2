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

    public static bool ClientStartsItself(QuestMetadata metadata, int mapId) {
        QuestMetadataBasic basic = metadata.Basic;
        return ClientStartsItself(basic.Type, basic.AutoStart, basic.Disabled, basic.ProgressMaps, mapId);
    }
}
