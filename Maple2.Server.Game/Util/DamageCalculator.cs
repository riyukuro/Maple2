using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Formulas;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Game.Util;

public static class DamageCalculator {
    // Npcs (not pets) use their own factor; players and pets keep the unconfirmed 4.
    internal static double AttackDamageFactor(bool npcCaster) => npcCaster ? Constant.NpcAttackDamageFactor : Constant.AttackDamageFactor;

    // The part of its full hit an npc (not a pet) deals, for a roll in [0, 1): even between 0.96 and 1.
    internal static double NpcAttackRoll(double roll) => 1 - Constant.NpcAttackDamageSpread * roll;

    // A shield takes a hit up to its value: what is left of the shield, what is left of the hit, and whether the
    // shield broke (the hit was larger than the shield).
    public static (long ShieldLeft, double DamageLeft, bool Broken) AbsorbShield(long shield, double damage) {
        if (shield >= damage) {
            return (shield - (long) damage, 0, false);
        }
        return (0, damage - shield, true);
    }

    // The target's reduction of a hit's range type (item option and buff lines "Melee Reduction" 37 and "Ranged
    // Damage Reduction" 38), none for a hit of neither.
    internal static SpecialAttribute? RangeReductionOf(RangeType rangeType) => rangeType switch {
        RangeType.Melee => SpecialAttribute.ReduceMeleeDamage,
        RangeType.Range => SpecialAttribute.ReduceRangedDamage,
        _ => null,
    };

    // The target's reduction of a hit's element (the "... Damage Reduction" lines 28 to 33), none for a physical or
    // element-less hit.
    internal static SpecialAttribute? ElementReductionOf(Element element) => element switch {
        Element.Fire => SpecialAttribute.ReduceFireDamage,
        Element.Ice => SpecialAttribute.ReduceIceDamage,
        Element.Electric => SpecialAttribute.ReduceElectricDamage,
        Element.Holy => SpecialAttribute.ReduceHolyDamage,
        Element.Dark => SpecialAttribute.ReduceDarkDamage,
        Element.Poison => SpecialAttribute.ReducePoisonDamage,
        _ => null,
    };

    // The caster's physical or magical damage rate (a buff's OffensiveProperty papDamageR or mapDamageR plus the invoke
    // bonuses on them) raises its hits by that rate: the effect tooltips read 0.021 as "damage dealt +2.1 %".
    internal static double DamageRateFactor(double rate) => 1 + rate;

    // The target's damage taken rate of the hit's attack type (a buff's DefensiveProperty papDamageR or mapDamageR,
    // summed over the buffs held) takes that share of the hit away: the effect tooltips read 0.99 as "damage taken
    // from enemies -99 %" and 1 as "blocks physical damage". A negative rate adds to the hit (-0.5 takes 1.5 times);
    // a rate of 1 or more leaves nothing, so the hit keeps only its least.
    internal static double DamageTakenFactor(double rate) => Math.Max(0, 1 - rate);

    // The part of a hit the target takes after its range and element reductions (rates, 0.022 for 2.2 %). Each
    // reduction takes its part of what is left, and one of 100 % or more takes the whole hit. The client never
    // computes damage, so the server chooses the formula.
    internal static double ReductionFactor(double rangeReduction, double elementReduction) {
        return (1 - Math.Clamp(rangeReduction, 0, 1)) * (1 - Math.Clamp(elementReduction, 0, 1));
    }

    internal static double ReductionFactor(IActor target, RangeType rangeType, Element element) {
        double Rate(SpecialAttribute? attribute) => attribute is { } value ? target.Stats.Values[value].Multiplier() : 0;
        return ReductionFactor(Rate(RangeReductionOf(rangeType)), Rate(ElementReductionOf(element)));
    }

    // The part of the target's maximum health a hit by target max hp (a skill attack's or a dot's
    // damageByTargetMaxHP) takes, cut by the caster's Reduce Damage By Target Max HP line (reduce_damage_by_
    // targetmaxhp). Intimidation's debuffs (10200061, 10200064: 9 % to 42 %, with the same cut to attack) and
    // 10200184 (50 %) put it on the monsters a Berserker hits, so their health-share hits weaken with their attack;
    // nothing read it before. 100 % or more leaves nothing; a negative rate adds nothing. The client never computes
    // damage, so the server chooses the formula.
    internal static long TargetMaxHpDamage(IActor caster, IActor target, float rate) {
        if (rate <= 0) {
            return 0;
        }
        double cut = Math.Clamp(caster.Stats.Values[SpecialAttribute.ReduceDamageByTargetMaxHp].Multiplier(), 0, 1);
        return (long) (target.Stats.Values[BasicAttribute.Health].Total * rate * (1 - cut));
    }

    // The caster's bonus against an npc of the target's rank: Common Monster Damage (s_item_opt_sa_improve_damage_
    // normalNpc), Elite Monster Damage (_namedNpc) or Bonus Boss Damage (_bossNpc). The ranks are those of the server
    // table monsterExpTable's columns (MonsterExpMetadata.ForClass): class 3 elite and 7 dungeon elite, 4 boss and 6
    // dungeon boss, any other class common. None against a friendly npc or (no npc) a player. Before, the boss bonus
    // also went to elites, nothing read the elite one, and the common one went to every hit. The client never
    // computes damage. Leader Monster Damage (_leaderNpc) is on no item or buff in the KR data and no class is a
    // leader, so it stays unread.
    internal static SpecialAttribute? RankBonusOf(int friendly, int npcClass) {
        if (friendly != 0) {
            return null;
        }
        return npcClass switch {
            3 or 7 => SpecialAttribute.EliteNpcDamage,
            4 or 6 => SpecialAttribute.BossNpcDamage,
            _ => SpecialAttribute.NormalNpcDamage,
        };
    }

    // The caster's rank bonus rate against the target (RankBonusOf), 0 against a player.
    internal static double RankBonus(IActor caster, IActor target) {
        if (target is not FieldNpc npc || RankBonusOf(npc.Value.Metadata.Basic.Friendly, npc.Value.Metadata.Basic.Class) is not { } attribute) {
            return 0;
        }
        return caster.Stats.Values[attribute].Multiplier();
    }

    public static (DamageType, long) CalculateDamage(IActor caster, IActor target, DamagePropertyRecord property) {
        // Check block
        if (Damage.TriggerCompulsionEvent(target.Buffs.TotalCompulsionRate(BuffCompulsionEventType.BlockChance, property.SkillId))) {
            return (DamageType.Block, 0);
        }

        // Check evase
        if (Damage.TriggerCompulsionEvent(target.Buffs.TotalCompulsionRate(BuffCompulsionEventType.EvasionChanceOverride, property.SkillId))) {
            return (DamageType.Miss, 0);
        }

        // Get hit rate
        if (Damage.Evade(target.Buffs.GetResistance(BasicAttribute.Accuracy), target.Stats.Values[BasicAttribute.Evasion].Total, target.Stats.Values[BasicAttribute.Dexterity].Total,
                caster.Stats.Values[BasicAttribute.Accuracy].Total, caster.Buffs.GetResistance(BasicAttribute.Evasion), caster.Stats.Values[BasicAttribute.Luck].Total, property.SuperArmorBreak)) {
            return (DamageType.Miss, 0);
        }

        var damageType = DamageType.Normal;
        (double minBonusAtkDamage, double maxBonusAtkDamage) = caster.Stats.GetBonusAttack(target.Buffs.GetResistance(BasicAttribute.BonusAtk), target.Buffs.GetResistance(BasicAttribute.MaxWeaponAtk));
        double attackDamage = caster is FieldNpc and not FieldPet
            ? NpcAttackRoll(Random.Shared.NextDouble())
            : minBonusAtkDamage + (maxBonusAtkDamage - minBonusAtkDamage) * double.Lerp(Random.Shared.NextDouble(), Random.Shared.NextDouble(), Random.Shared.NextDouble());

        double damageBonus = 1 + caster.Stats.Values[SpecialAttribute.TotalDamage].Multiplier() + RankBonus(caster, target);

        // Get elemental bonus
        double elementBonus = 0;
        switch (property.Element) {
            case Element.Fire:
                elementBonus = caster.Stats.Values[SpecialAttribute.FireDamage].Multiplier();
                break;
            case Element.Ice:
                elementBonus = caster.Stats.Values[SpecialAttribute.IceDamage].Multiplier();
                break;
            case Element.Electric:
                elementBonus = caster.Stats.Values[SpecialAttribute.ElectricDamage].Multiplier();
                break;
            case Element.Holy:
                elementBonus = caster.Stats.Values[SpecialAttribute.HolyDamage].Multiplier();
                break;
            case Element.Dark:
                elementBonus = caster.Stats.Values[SpecialAttribute.DarkDamage].Multiplier();
                break;
            case Element.Poison:
                elementBonus = caster.Stats.Values[SpecialAttribute.PoisonDamage].Multiplier();
                break;
        }
        damageBonus += elementBonus;

        // Get range bonus
        switch (property.RangeType) {
            case RangeType.None:
                break;
            case RangeType.Melee:
                damageBonus += caster.Stats.Values[SpecialAttribute.MeleeDamage].Multiplier();
                break;
            case RangeType.Range:
                damageBonus += caster.Stats.Values[SpecialAttribute.RangedDamage].Multiplier();
                break;
        }

        damageBonus += -target.Buffs.GetResistance(BasicAttribute.AttackSpeed) * caster.Stats.Values[BasicAttribute.AttackSpeed].Total;

        // Check for crit and get crit damage
        if (property.CanCrit) {
            if (property.CompulsionTypes.Contains(CompulsionType.Critical)) {
                damageType = DamageType.Critical;
            } else {
                damageType = caster.Stats.GetCriticalRate(target.Stats.Values[BasicAttribute.CriticalEvasion].Total, property.SkillId);
            }
        }

        damageBonus *= damageType == DamageType.Critical ? caster.Stats.GetCriticalDamage(target.Buffs.GetResistance(BasicAttribute.CriticalDamage)) : 1;

        // Get invoked buff values
        // A dot's own invokes (type 5) are in its rate already (DotDamageRecord.DotRate).
        (int invokeValue, float invokeRate) = caster.Buffs.GetInvokeValues(InvokeEffectType.IncreaseSkillDamage, property.SkillId, property.SkillGroup);

        double damageMultiplier = damageBonus * (1 + invokeRate) * (property.Rate + invokeValue);

        double defensePierce = 1 - Math.Min(0.3, (1 / (1 + target.Buffs.GetResistance(BasicAttribute.Piercing)) * (caster.Stats.Values[BasicAttribute.Piercing].Multiplier() - 1)));
        damageMultiplier *= 1 / (Math.Max(target.Stats.Values[BasicAttribute.Defense].Total, 1) * defensePierce);

        // Check resistances
        double attackTypeAmount = 0;
        double resistance = 0;
        double damageRate = 0;
        // Physical or magical, for the target's damage taken rate; neither when the attack type is neither.
        bool? physicalHit = null;
        switch (property.AttackType) {
            case AttackType.Physical:
                resistance = Damage.CalculateResistance(target.Stats.Values[BasicAttribute.PhysicalRes].Total, caster.Stats.Values[SpecialAttribute.PhysicalPiercing].Multiplier());
                attackTypeAmount = caster.Stats.Values[BasicAttribute.PhysicalAtk].Total;
                damageRate = caster.Stats.Values[SpecialAttribute.OffensivePhysicalDamage].Multiplier();
                physicalHit = true;
                break;
            case AttackType.Magic:
                resistance = Damage.CalculateResistance(target.Stats.Values[BasicAttribute.MagicalRes].Total, caster.Stats.Values[SpecialAttribute.MagicalPiercing].Multiplier());
                attackTypeAmount = caster.Stats.Values[BasicAttribute.MagicalAtk].Total;
                damageRate = caster.Stats.Values[SpecialAttribute.OffensiveMagicalDamage].Multiplier();
                physicalHit = false;
                break;
            // If all, calculate the higher of the two
            case AttackType.All:
                BasicAttribute attackTypeAttribute = caster.Stats.Values[BasicAttribute.PhysicalAtk].Total >= caster.Stats.Values[BasicAttribute.MagicalAtk].Total
                    ? BasicAttribute.PhysicalAtk
                    : BasicAttribute.MagicalAtk;
                SpecialAttribute piercingAttribute = attackTypeAttribute == BasicAttribute.PhysicalAtk
                    ? SpecialAttribute.PhysicalPiercing
                    : SpecialAttribute.MagicalPiercing;
                SpecialAttribute damageRateAttribute = attackTypeAttribute == BasicAttribute.PhysicalAtk
                    ? SpecialAttribute.OffensivePhysicalDamage
                    : SpecialAttribute.OffensiveMagicalDamage;
                attackTypeAmount = Math.Max(caster.Stats.Values[BasicAttribute.PhysicalAtk].Total, caster.Stats.Values[BasicAttribute.MagicalAtk].Total) * 0.5f;
                resistance = Damage.CalculateResistance(target.Stats.Values[SkillCastRules.ResistanceOf(attackTypeAttribute)].Total, caster.Stats.Values[piercingAttribute].Multiplier());
                damageRate = caster.Stats.Values[damageRateAttribute].Multiplier();
                physicalHit = attackTypeAttribute == BasicAttribute.PhysicalAtk;
                break;
        }

        damageMultiplier *= attackTypeAmount * resistance * DamageRateFactor(damageRate);
        attackDamage *= damageMultiplier * AttackDamageFactor(caster is FieldNpc and not FieldPet) + property.Value;
        attackDamage *= ReductionFactor(target, property.RangeType, property.Element);
        if (physicalHit is bool physical) {
            attackDamage *= DamageTakenFactor(target.Buffs.DamageTakenRate(physical));
        }

        // Apply any shields
        foreach (Buff buff in BuffManager.Shields(target.Buffs.EnumerateBuffs())) {
            (long shieldLeft, double damageLeft, bool broken) = AbsorbShield(buff.ShieldHealth, attackDamage);
            if (!broken) {
                buff.ShieldHealth = shieldLeft;
                // The client reads the shield value only with the shield flag set.
                target.Field.Broadcast(BuffPacket.Update(buff, BuffFlag.UpdateBuff | BuffFlag.UpdateShield));
                return (DamageType.Block, 0);
            }

            attackDamage = damageLeft;
            target.Buffs.Remove(buff.Id, buff.Caster.ObjectId);
        }

        return (damageType, (long) Math.Max(1, attackDamage));
    }
}
