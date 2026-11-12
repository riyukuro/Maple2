using Maple2.Model.Enum;
using Maple2.Model.Game;

namespace Maple2.Server.Game.Util;

// Checks on a player's skill cast that the client makes itself and the server repeats before a cost is paid.
public static class SkillCastRules {
    // Chosen here: a use that comes up to this long before the cooldown's end is still taken, so a client whose
    // clock runs a little ahead is not refused. KMS2 takes a second use 4.0 s after the first on a 5 s cooldown.
    public const long CooldownSlackMs = 1500;

    public static bool RefusedDead(bool isDead, bool allowDead) => isDead && !allowDead;

    public static bool OnCooldown(long tick, long endTick) => tick < endTick - CooldownSlackMs;

    // The cooldown that refuses a use of the skill at this tick: its own, or that of a skill of the same cooldown
    // group (group 0 is no group). The one that ends last when several do.
    public static SkillCooldown? BlockingCooldown(IEnumerable<SkillCooldown> cooldowns, int skillId, int groupId, long tick) {
        SkillCooldown? blocking = null;
        foreach (SkillCooldown cooldown in cooldowns) {
            if (cooldown.SkillId != skillId && (groupId == 0 || cooldown.GroupId != groupId)) {
                continue;
            }
            if (OnCooldown(tick, cooldown.EndTick) && (blocking == null || cooldown.EndTick > blocking.EndTick)) {
                blocking = cooldown;
            }
        }
        return blocking;
    }

    // A cast takes its item once, however often its cost is asked for.
    public static bool ConsumesItem(bool useItem, bool itemConsumed) => useItem && !itemConsumed;

    // The target's resistance against an attack counted by this attack stat.
    public static BasicAttribute ResistanceOf(BasicAttribute attack) {
        return attack == BasicAttribute.MagicalAtk ? BasicAttribute.MagicalRes : BasicAttribute.PhysicalRes;
    }
}
