using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model;

namespace Maple2.Server.Game.Util;

// A skill range's target choice, as the client's search makes it (CBaseCollision, after the shape test and before the
// target count): the buff, party and guild filter of targetSelectType 2 and 3, then the stat sort of
// targetStatCompareFunction. The caller takes the target count from the result.
public static class RangeTargetSelect {
    public static bool Active(this SkillMetadataRangeSelect? select) => select is { Mode: 2 or 3 } or { StatSort: not 0 };

    public static IEnumerable<IActor> Apply(SkillMetadataRangeSelect? select, IActor caster, IEnumerable<IActor> pool) {
        if (select == null) {
            return pool;
        }
        List<IActor> targets = pool.ToList();
        if (select.Mode is 2 or 3) {
            List<IActor> passed = targets.Where(target => Passes(select, caster, target)).ToList();
            // Mode 2 keeps only the passing targets; mode 3 keeps them if there are any, else all.
            if (select.Mode == 2 || passed.Count > 0) {
                targets = passed;
            }
        }
        return select.StatSort switch {
            1 => targets.OrderBy(target => Current(target, select.Stat)).ToList(),
            2 => targets.OrderByDescending(target => Current(target, select.Stat)).ToList(),
            3 => targets.OrderBy(target => Ratio(target, select.Stat)).ToList(),
            4 => targets.OrderByDescending(target => Ratio(target, select.Stat)).ToList(),
            _ => targets,
        };
    }

    public static bool Passes(SkillMetadataRangeSelect select, IActor caster, IActor target) {
        if (select.BuffId != 0 && !target.Buffs.EnumerateBuffs(select.BuffId)
                .Any(buff => buff.Enabled && (!select.BuffFromCaster || buff.Caster.ObjectId == caster.ObjectId))) {
            return false;
        }
        // The party and guild tests apply between two players only; a party or guild of 0 matches no one.
        if (caster is FieldPlayer casterPlayer && target is FieldPlayer targetPlayer) {
            Maple2.Model.Game.Character owner = casterPlayer.Value.Character;
            Maple2.Model.Game.Character other = targetPlayer.Value.Character;
            if (select.SameParty && (owner.PartyId == 0 || owner.PartyId != other.PartyId)) {
                return false;
            }
            if (select.SameGuild && (owner.GuildId == 0 || owner.GuildId != other.GuildId)) {
                return false;
            }
        }
        return true;
    }

    private static long Current(IActor actor, BasicAttribute stat) => actor.Stats.Values[stat].Current;

    private static float Ratio(IActor actor, BasicAttribute stat) {
        var value = actor.Stats.Values[stat];
        return value.Total > 0 ? (float) value.Current / value.Total : 0f;
    }
}
