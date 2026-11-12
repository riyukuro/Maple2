using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Util;
using Serilog;

namespace Maple2.Server.Game.Model.Skill;

public class DotDamageRecord {
    public readonly IActor Caster;
    public readonly IActor Target;

    public readonly DamagePropertyRecord Properties;
    public int BuffObjectId { get; init; }

    public readonly DamageType Type;
    public readonly int HpAmount;
    public readonly int SpAmount;
    public readonly int EpAmount;
    public readonly int RecoverHp;
    // The tick's damage was taken away by an invincible buff of the target; the 0 is still sent.
    public readonly bool Invincible;

    public DotDamageRecord(IActor caster, IActor target, AdditionalEffectMetadataDot.DotDamage dotDamage, int stacks = 1) {
        Caster = caster;
        Target = target;
        Properties = new DamagePropertyRecord {
            CanCrit = dotDamage.UseGrade,
            Element = dotDamage.Element,
            AttackType = dotDamage.Type,
            Rate = dotDamage.Rate,
        };
        Type = DamageType.Normal;

        // The whole amount is times the buff's stack count (Buff.AmountFactor), before the not-kill cap.
        long hpAmount = dotDamage.HpValue;
        if (!dotDamage.IsConstDamage) {
            // An invincible target's tick is not rolled, so its shields stay.
            if (dotDamage.Type != AttackType.None && !target.Buffs.Invincible) {
                try {
                    (DamageType type, long amount) = DamageCalculator.CalculateDamage(caster, target, Properties);
                    Type = type;
                    hpAmount += amount;
                } catch (Exception e) {
                    Log.Logger.Error(e, "Error calculating damage for {Caster} on {Target} with dot damage {DotDamage}", caster, Target, dotDamage);
                    hpAmount = 0; // Fallback to 0 if calculation fails
                }
            }
            hpAmount += (long) (dotDamage.DamageByTargetMaxHp * Target.Stats.Values[BasicAttribute.Health].Total);
        }
        hpAmount = Buff.AmountByStacks(hpAmount, stacks);
        if (hpAmount > 0 && target.Buffs.Invincible) {
            // A tick on a holder of an invincible buff is sent as Normal with amount 0.
            hpAmount = 0;
            Invincible = true;
        }
        if (dotDamage.NotKill) {
            hpAmount = Math.Min(hpAmount, Target.Stats.Values[BasicAttribute.Health].Current - 1);
        }

        HpAmount = (int) -hpAmount;
        SpAmount = -Buff.AmountByStacks(dotDamage.SpValue, stacks);
        EpAmount = -Buff.AmountByStacks(dotDamage.EpValue, stacks);
        RecoverHp = (int) (dotDamage.RecoverHpByDamage * HpAmount);
    }
}
