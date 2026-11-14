using System.Text.Json.Serialization;
using Maple2.Model.Enum;

namespace Maple2.Model.Metadata;

// The server table promotionContents.xml without its Snowman rows (those are the snowman event's snowflake sources,
// GameEventTable Snowman.Sources): while a row runs (StartTime to EndTime, the server's local clock), each time the
// player's condition Type is counted with the row's code and target, the row gives its individual drop box with the
// chance Prop / 10000, to a player of at least RequireLevel, while the counter of its ShareId (rewardLimitShareID, else
// its own id) is below its LimitCount for the period of LimitType. The counter is the account's when IsAccount is set,
// else the character's. Kind is the row's promotionType, sent as the TaskEvent type of the reward notice.
public record PromotionTable(PromotionTable.Entry[] Entries) : ServerTable {
    public record Entry(
        int Id,
        PromotionKind Kind,
        ConditionMetadata Condition,
        DateTime StartTime,
        DateTime EndTime,
        int Prop,
        int IndividualDropBoxId,
        int RequireLevel,
        PromotionLimit LimitType,
        int LimitCount,
        int ShareId,
        bool IsAccount);

    private ILookup<ConditionType, Entry>? byType;

    // The rows of a condition type.
    [JsonIgnore]
    public ILookup<ConditionType, Entry> ByType => byType ??= Entries.ToLookup(entry => entry.Condition.Type);
}

// promotionType: the TaskEvent (0xF9) type of the reward notice, as CSceneStateGame reads it (scenestategame.cpp:9280):
// 0 lists the items, 1 to 3 show the npc's portrait and its s_<npc>_promotion_reward text with each item's name.
public enum PromotionKind : byte {
    normal = 0,
    goldus = 1,
    pesca = 2,
    stefan = 3,
}

// rewardLimitType: the period a row's counter lasts. none: no limit (an empty type or count).
public enum PromotionLimit : byte {
    none = 0,
    day = 1,
    week = 2,
    eventPeriod = 3,
}
