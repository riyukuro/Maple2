using Maple2.Model.Enum;
using Maple2.Model.Metadata;

namespace Maple2.Server.Game.Manager;

// The resistance and invoke sums of the buffs an actor holds, taken once and kept until a buff is added, removed or
// changes its stack count (BuffManager.InvalidateSums). A hit reads the resistances six times and more; this keeps it
// from walking every buff each time. Only the buffs with an invoke effect are kept for the invoke sums.
public sealed class BuffSums {
    private readonly Dictionary<BasicAttribute, float> resistances = [];
    private readonly (AdditionalEffectMetadataInvokeEffect? Invoke, int Stacks)[] invokes;

    // One of the buffs held has the invincible flag.
    public bool Invincible { get; }
    // One of them has the invincible value 2. Chosen here for a holder of buffs of both values: this one counts.
    public bool InvincibleReflect { get; }
    // One of them has a stun value that stuns a monster (NpcControlRules.Stuns).
    public bool Stun { get; }
    // One of them has deathResistanceHP (the Berserker's Raging Soul, 10200221): damage leaves its holder at 1 health.
    public bool DeathResistance { get; }

    public BuffSums(IEnumerable<(AdditionalEffectMetadata Metadata, int Stacks)> held) {
        List<(AdditionalEffectMetadataInvokeEffect? Invoke, int Stacks)> withInvoke = [];
        foreach ((AdditionalEffectMetadata metadata, int stacks) in held) {
            foreach ((BasicAttribute attribute, float value) in metadata.Status.Resistances) {
                resistances[attribute] = resistances.GetValueOrDefault(attribute) + Model.Buff.ByStacks(value, stacks);
            }
            Invincible |= metadata.Status.Invincible;
            Stun |= metadata.Property is { } property && Util.NpcControlRules.Stuns(property.Stun);
            DeathResistance |= metadata.Status.DeathResistanceHp;
            InvincibleReflect |= metadata.Status.Invincible && metadata.Status.InvincibleReflect;
            if (metadata.InvokeEffect != null) {
                withInvoke.Add((metadata.InvokeEffect, stacks));
            }
        }
        invokes = withInvoke.ToArray();
    }

    public float Resistance(BasicAttribute attribute) => resistances.GetValueOrDefault(attribute);

    public (int, float) Invokes(InvokeEffectType invokeType, int skillId, params int[] skillGroup) {
        return invokes.Length == 0 ? (0, 0f) : BuffManager.SumInvokes(invokes, invokeType, skillId, skillGroup);
    }

    public (int, float) EffectInvokes(InvokeEffectType invokeType, int effectId, int effectGroupId) {
        return invokes.Length == 0 ? (0, 0f) : BuffManager.SumEffectInvokes(invokes, invokeType, effectId, effectGroupId);
    }
}
