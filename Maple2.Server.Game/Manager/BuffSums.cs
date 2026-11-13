using Maple2.Model.Enum;
using Maple2.Model.Metadata;

namespace Maple2.Server.Game.Manager;

// The resistance and invoke sums of the buffs an actor holds, taken once and kept until a buff is added, removed or
// changes its stack count (BuffManager.InvalidateSums). A hit reads the resistances six times and more; this keeps it
// from walking every buff each time. Only the buffs with an invoke effect are kept for the invoke sums.
public sealed class BuffSums {
    private readonly Dictionary<BasicAttribute, float> resistances = [];
    private readonly (AdditionalEffectMetadataInvokeEffect? Invoke, int Stacks)[] invokes;
    // The attackPossible limits of the buffs held (AttackPossibleRules); empty for most actors, so a hit checks nothing.
    private readonly AttackPossibleMetadata[] attackLimits;

    // One of the buffs held has the invincible flag.
    public bool Invincible { get; }
    // One of them has the invincible value 2. Chosen here for a holder of buffs of both values: this one counts.
    public bool InvincibleReflect { get; }
    // One of them has a stun value that stuns a monster (NpcControlRules.Stuns).
    public bool Stun { get; }
    // The highest abnormalImmuneBreak among those stun buffs; -1 when none is held (NpcControlRules.StunTakes).
    public int StunBreak { get; } = -1;
    // One of them has deathResistanceHP (the Berserker's Raging Soul, 10200221): damage leaves its holder at 1 health.
    public bool DeathResistance { get; }
    // The damage taken rates of the buffs held (DefensiveProperty papDamageR, mapDamageR), each times its stack count.
    public float PhysicalDamageTaken { get; }
    public float MagicalDamageTaken { get; }
    // The recovery rates of the buffs held (StatusProperty itemRecoveryHPRate, healRecoveryHPRate), each times its
    // stack count.
    public float ItemRecovery { get; }
    public float HealRecovery { get; }

    public BuffSums(IEnumerable<(AdditionalEffectMetadata Metadata, int Stacks)> held) {
        List<(AdditionalEffectMetadataInvokeEffect? Invoke, int Stacks)> withInvoke = [];
        List<AttackPossibleMetadata> limits = [];
        foreach ((AdditionalEffectMetadata metadata, int stacks) in held) {
            foreach ((BasicAttribute attribute, float value) in metadata.Status.Resistances) {
                resistances[attribute] = resistances.GetValueOrDefault(attribute) + Model.Buff.ByStacks(value, stacks);
            }
            Invincible |= metadata.Status.Invincible;
            if (metadata.Property is { } property && Util.NpcControlRules.Stuns(property.Stun)) {
                Stun = true;
                StunBreak = Math.Max(StunBreak, property.ImmuneBreak);
            }
            DeathResistance |= metadata.Status.DeathResistanceHp;
            InvincibleReflect |= metadata.Status.Invincible && metadata.Status.InvincibleReflect;
            PhysicalDamageTaken += Model.Buff.ByStacks(metadata.Status.PhysicalDamageTakenRate, stacks);
            MagicalDamageTaken += Model.Buff.ByStacks(metadata.Status.MagicalDamageTakenRate, stacks);
            ItemRecovery += Model.Buff.ByStacks(metadata.Status.ItemRecoveryRate, stacks);
            HealRecovery += Model.Buff.ByStacks(metadata.Status.HealRecoveryRate, stacks);
            if (metadata.InvokeEffect != null) {
                withInvoke.Add((metadata.InvokeEffect, stacks));
            }
            if (metadata.Property?.AttackPossible is { } limit) {
                limits.Add(limit);
            }
        }
        invokes = withInvoke.ToArray();
        attackLimits = limits.ToArray();
    }

    // A hit (skillId) or a damage over time tick (dotEffectId) of the caster is refused by an attackPossible limit held.
    public bool HasAttackLimits => attackLimits.Length != 0;

    public bool AttackRefused(int skillId, int dotEffectId, Func<int, bool> casterHolds) {
        return attackLimits.Length != 0 && !Util.AttackPossibleRules.Allows(attackLimits, skillId, dotEffectId, casterHolds);
    }

    public float Resistance(BasicAttribute attribute) => resistances.GetValueOrDefault(attribute);

    public (int, float) Invokes(InvokeEffectType invokeType, int skillId, params int[] skillGroup) {
        return invokes.Length == 0 ? (0, 0f) : BuffManager.SumInvokes(invokes, invokeType, skillId, skillGroup);
    }

    public (int, float) EffectInvokes(InvokeEffectType invokeType, int effectId, int effectGroupId) {
        return invokes.Length == 0 ? (0, 0f) : BuffManager.SumEffectInvokes(invokes, invokeType, effectId, effectGroupId);
    }
}
