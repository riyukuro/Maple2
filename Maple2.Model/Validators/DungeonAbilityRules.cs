using Maple2.Model.Enum;

namespace Maple2.Model.Validators;

// The special abilities that count only in one dungeon group. The client names them in tooltips and has no other
// reader; the server picks where and how they count (FORK.md). KR rolls them as merge options (itemmergeoptionbase
// 3703xxxx and 3704xxxx), and the gear of itemoptionconstant 11600801 onwards has the Dark Stream two as constants.
// specialabilitystringtable gives each its value type: 2 a rate in thousandths ("+{0:0.1f}%"), 1 a value.
//  - Chaos Raid (group chaosRaid): 147 improve_chaosraid_wap a rate of weapon attack, 148 _asp attack speed points,
//    149 _atp accuracy, 150 _hp health.
//  - Dark Stream (group darkStream): 109 improve_darkstream_damage a rate added to the damage dealt, 110
//    reduce_darkstream_recive_damage a rate taken off the damage received.
// 111 improve_darkstream_evp is in no KR table, item or effect, so it stays unread.
public static class DungeonAbilityRules {
    public static bool ChaosRaid(DungeonGroupType? group) => group == DungeonGroupType.chaosRaid;

    public static bool DarkStream(DungeonGroupType? group) => group == DungeonGroupType.darkStream;

    // The stats are rebuilt at a field entry that starts or ends a Chaos Raid room's lines: nothing else rebuilds them
    // there (a rebuild while leaving still sees the old field). The Dark Stream lines are read at each hit.
    public static bool RebuildsOnEntry(DungeonGroupType? left, DungeonGroupType? entered) => ChaosRaid(left) != ChaosRaid(entered);

    // The share of a hit a target in Dark Stream takes: 70 (7 %) takes 93 %. A rate of 100 % or more leaves nothing,
    // a negative one adds nothing.
    public static double ReceivedFactor(long reduceRate) => 1 - Math.Clamp(reduceRate / 1000.0, 0, 1);
}
