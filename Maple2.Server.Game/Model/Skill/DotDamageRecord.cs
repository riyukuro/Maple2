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
    // A typed dot that is not constant damage sends its tick even at 0 (a dot with no rate, share or value, as KR
    // 49289032, 70002111 and Priest 10400221 lv1-12): the client's tick (PC::vfunc_34) flashes the hit color, plays the
    // effect's invokeEvent 0 effects and its hit sound.
    public readonly bool Typed;

    // The tick is sent: it takes health, spirit or stamina, an invincible buff took it, or it is a typed dot's.
    public bool Sends => HpAmount != 0 || SpAmount != 0 || EpAmount != 0 || Invincible || Typed;

    /// <param name="invokeRate">The caster's dot damage invokes (type 5) on the effect: they raise its rate (DotRate).</param>
    /// <param name="effectId">The dot's effect: an attackPossible limit of the target lets only the dots it names hit.</param>
    public DotDamageRecord(IActor caster, IActor target, AdditionalEffectMetadataDot.DotDamage dotDamage, int stacks = 1, float invokeRate = 0f, int effectId = 0) {
        Caster = caster;
        Target = target;
        Properties = new DamagePropertyRecord {
            CanCrit = dotDamage.UseGrade,
            Element = dotDamage.Element,
            AttackType = dotDamage.Type,
            Rate = DotRate(dotDamage.Rate, invokeRate),
        };
        Type = DamageType.Normal;
        Typed = dotDamage.Type != AttackType.None && !dotDamage.IsConstDamage;
        // A target that takes no damage: an invincible buff, or an attackPossible limit that refuses this dot.
        bool shielded = target.Buffs.Invincible || target.Buffs.AttackRefused(caster, 0, effectId);

        // The whole amount is times the buff's stack count (Buff.AmountFactor), before the not-kill cap.
        long hpAmount = dotDamage.HpValue;
        if (!dotDamage.IsConstDamage) {
            // An invincible target's tick is not rolled, so its shields stay. A typed dot without a rate has no rolled part
            // (rate 0 times any attack is 0): it is not rolled either, so it gets no Miss, no crit and not the hit's least
            // damage of 1 on top of its share. The 14 typed dots the KR triggers, regions and cubes add are all such (e.g.
            // 70000114: Type 1, DamageByTargetMaxHp 0.2), as are 149 of the KR dot levels.
            if (dotDamage.Type != AttackType.None && dotDamage.Rate > 0 && !shielded) {
                try {
                    (DamageType type, long amount) = DamageCalculator.CalculateDamage(caster, target, Properties);
                    Type = type;
                    hpAmount += amount;
                } catch (Exception e) {
                    Log.Logger.Error(e, "Error calculating damage for {Caster} on {Target} with dot damage {DotDamage}", caster, Target, dotDamage);
                    hpAmount = 0; // Fallback to 0 if calculation fails
                }
            }
            hpAmount += DamageCalculator.TargetMaxHpDamage(caster, target, dotDamage.DamageByTargetMaxHp);
        }
        hpAmount = Buff.AmountByStacks(hpAmount, stacks);
        if (hpAmount > 0 && shielded) {
            // A tick on a holder of an invincible buff (or a refused one) is sent as Normal with amount 0.
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

    // Invoke type 5 (IncreaseDotDamage): the invokes keyed on a dot's effect or its effect group raise the dot's
    // damage rate by their summed rate, as the client's DPS meter counts the caster's own dot tick (rate + rate *
    // the invoke rate, in float: CDPSMeterSystem::OnDotDamage, dpsmetersystem.cpp:1145). The client has no other
    // reader of the type; the played data's 161 rows all have a rate and no value (90050003: 0.35 on 90050002).
    public static float DotRate(float rate, float invokeRate) => rate + rate * invokeRate;
}
