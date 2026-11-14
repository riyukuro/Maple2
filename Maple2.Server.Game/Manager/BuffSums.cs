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
    // One of them has pvpOnOff (the shadow world's entry protection 70000032, and 70000029): on a pk field the holder
    // and the other players are not hostile to each other (PC::vfunc_122, pc.cpp:3112).
    public bool PvpOnOff { get; }
    // The damage taken rates of the buffs held (DefensiveProperty papDamageR, mapDamageR), each times its stack count.
    public float PhysicalDamageTaken { get; }
    public float MagicalDamageTaken { get; }
    // The recovery rates of the buffs held (StatusProperty itemRecoveryHPRate, healRecoveryHPRate), each times its
    // stack count.
    public float ItemRecovery { get; }
    public float HealRecovery { get; }

    public BuffSums(IEnumerable<(AdditionalEffectMetadata Metadata, int Stacks)> held)
        : this(held.Select(buff => (buff.Metadata, buff.Stacks, false))) { }

    // A suspended buff (Buff.Suspended: its holder rides a battle mount) keeps its stun, invoke and attackPossible
    // parts and gives none of the status ones (AdditionalCliList::FUN_140972a00 skips it, FUN_1409713b0 and
    // CollectInvokeEffectStats do not).
    public static BuffSums Of(IEnumerable<(AdditionalEffectMetadata Metadata, int Stacks, bool Suspended)> held) => new(held);

    private BuffSums(IEnumerable<(AdditionalEffectMetadata Metadata, int Stacks, bool Suspended)> held) {
        List<(AdditionalEffectMetadataInvokeEffect? Invoke, int Stacks)> withInvoke = [];
        List<AttackPossibleMetadata> limits = [];
        foreach ((AdditionalEffectMetadata metadata, int stacks, bool suspended) in held) {
            if (metadata.Property is { } stunProperty && Util.NpcControlRules.Stuns(stunProperty.Stun)) {
                Stun = true;
                StunBreak = Math.Max(StunBreak, stunProperty.ImmuneBreak);
            }
            if (metadata.InvokeEffect != null) {
                withInvoke.Add((metadata.InvokeEffect, stacks));
            }
            if (metadata.Property?.AttackPossible is { } limit) {
                limits.Add(limit);
            }
            if (suspended) {
                continue;
            }
            foreach ((BasicAttribute attribute, float value) in metadata.Status.Resistances) {
                resistances[attribute] = resistances.GetValueOrDefault(attribute) + Model.Buff.ByStacks(value, stacks);
            }
            Invincible |= metadata.Status.Invincible;
            DeathResistance |= metadata.Status.DeathResistanceHp;
            PvpOnOff |= metadata.Property?.PvpOnOff == true;
            InvincibleReflect |= metadata.Status.Invincible && metadata.Status.InvincibleReflect;
            PhysicalDamageTaken += Model.Buff.ByStacks(metadata.Status.PhysicalDamageTakenRate, stacks);
            MagicalDamageTaken += Model.Buff.ByStacks(metadata.Status.MagicalDamageTakenRate, stacks);
            ItemRecovery += Model.Buff.ByStacks(metadata.Status.ItemRecoveryRate, stacks);
            HealRecovery += Model.Buff.ByStacks(metadata.Status.HealRecoveryRate, stacks);
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
