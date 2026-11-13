using Maple2.Model.Metadata;

namespace Maple2.Server.Game.Util;

// BasicProperty attackPossibleSkillIDs, attackPossibleDotEffectIDs and attackPossibleEffectID: a holder of such an effect
// takes only the hits it names. KR uses them on boss shields that only a set skill or a set dot breaks (62100016: skill
// 90003005, dot 120030007, a 95000 shield; 70002401: skills 70003091/111/121/122/124, dots 70002271/272) and on an event
// effect that only holders of 90000874 hit (50004543). The client loads and compares the values and reads them nowhere
// else (additionalactionadditional.cpp:1080, 960), so the server applies them. Chosen here: with several such effects
// held, a hit lands only if each of them lets it; a refused hit is sent as an invincible hit of amount 0 (skill hits) or a
// 0 tick (dots), as a hit on a holder of an invincible buff, and its shields stay.
public static class AttackPossibleRules {
    /// <param name="skillId">The skill of a skill hit, 0 for a damage over time tick.</param>
    /// <param name="dotEffectId">The effect of a damage over time tick, 0 for a skill hit.</param>
    /// <param name="casterHolds">Whether the caster holds an effect id.</param>
    public static bool Allows(IEnumerable<AttackPossibleMetadata> limits, int skillId, int dotEffectId, Func<int, bool> casterHolds) {
        foreach (AttackPossibleMetadata limit in limits) {
            if (!Allows(limit, skillId, dotEffectId, casterHolds)) {
                return false;
            }
        }
        return true;
    }

    public static bool Allows(AttackPossibleMetadata limit, int skillId, int dotEffectId, Func<int, bool> casterHolds) {
        if (skillId != 0 && limit.SkillIds.Contains(skillId)) {
            return true;
        }
        if (dotEffectId != 0 && limit.DotEffectIds.Contains(dotEffectId)) {
            return true;
        }
        return limit.EffectId != 0 && casterHolds(limit.EffectId);
    }
}
