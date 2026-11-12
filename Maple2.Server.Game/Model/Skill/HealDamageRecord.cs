using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Tools;

namespace Maple2.Server.Game.Model.Skill;

public class HealDamageRecord : IByteSerializable {
    public readonly IActor Caster;
    public readonly IActor Target;
    public readonly int OwnerId;
    public readonly int HpAmount;
    public readonly int SpAmount;
    public readonly int EpAmount;

    public const float CritMultiplier = 1.5f;

    public HealDamageRecord(IActor caster, IActor target, int ownerId, AdditionalEffectMetadataRecovery recovery, int stacks = 1) {
        Caster = caster;
        Target = target;
        OwnerId = ownerId;

        // A heal crits at the caster's critical rate against the target's critical evasion, as a hit does: KMS2
        // heals of a caster on itself crit 93 times in 1,380 (6.7 %), at each stat set the rate the formula gives
        // (6.2 % at luck 18, critical rate 53, evasion 50; 26 % at critical rate 305). Before, the evasion was 0 and
        // the rate ran to its 40 % cap for everyone. A crit heals for 1.5 times the caster-scaled part.
        float multiplier = 1f;
        if (!recovery.DisableCrit && caster.Stats.GetCriticalRate(target.Stats.Values[BasicAttribute.CriticalEvasion].Total) == DamageType.Critical) {
            multiplier = CritMultiplier;
        }

        // Times the buff's stack count as a dot is (Buff.AmountFactor); chosen here, no KMS2 heal of a stacked buff
        // is known.
        long casterPart = CasterPart(recovery.RecoveryRate, caster.Stats.Values[BasicAttribute.MagicalAtk].Current,
            caster.Stats.Values[SpecialAttribute.RecoveryBonus].Total, multiplier > 1f);
        HpAmount = Buff.AmountByStacks((long) (recovery.HpValue + recovery.HpRate * target.Stats.Values[BasicAttribute.Health].Total
                                           + casterPart), stacks);
        SpAmount = Buff.AmountByStacks((long) (recovery.SpValue + recovery.SpRate * target.Stats.Values[BasicAttribute.Spirit].Total * multiplier), stacks);
        EpAmount = Buff.AmountByStacks((long) (recovery.EpValue + recovery.EpRate * target.Stats.Values[BasicAttribute.Stamina].Total * multiplier), stacks);
    }

    // The part of a heal that scales with the caster: recovery rate x magic attack rounded down, then raised by the
    // caster's recovery bonus (s_item_opt_sa_improve_recovery_regen_doheal, in thousandths) and rounded down, then
    // times 1.5 on a crit and rounded down. KMS2: rate 0.9 at magic attack 272 heals 245 with a bonus of 5 (244.8 ->
    // 244 -> 245.2) and crits for 367; rate 6.08 at 809 heals 5,163 with a bonus of 50 (4,918 -> 5,163.9).
    public static long CasterPart(float recoveryRate, long magicalAtk, long recoveryBonus, bool crit) {
        long part = (long) (recoveryRate * magicalAtk);
        part += part * recoveryBonus / 1000;
        return crit ? (long) (part * CritMultiplier) : part;
    }

    public void WriteTo(IByteWriter writer) {
        writer.WriteInt(Caster.ObjectId);
        writer.WriteInt(Target.ObjectId);
        writer.WriteInt(OwnerId);
        writer.WriteInt(HpAmount);
        writer.WriteInt(SpAmount);
        writer.WriteInt(EpAmount);
    }
}
