using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Model;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game;

// A held buff whose begin condition fails (Buff.Enabled false) counts for no stat, resistance, invoke, invincible or
// stun, as the client's sums skip a buff without m_bEnabled (AdditionalCliList::FUN_140972a00).
public class DisabledBuffTests {
    private static readonly AdditionalEffectMetadataProperty Property = new(BuffType.Buff, BuffSubType.Status, BuffCategory.None,
        BuffEventType.None, 0, 1000, 0, 0, 1, false, false, false, false, false, false, false, 0, 0, 0, 0, 0,
        BuffKeepCondition.TimerDuration, default, default, 0);

    internal static AdditionalEffectMetadata Effect(int id, Dictionary<BasicAttribute, long>? values = null,
            Dictionary<SpecialAttribute, float>? specialRates = null, Dictionary<BasicAttribute, float>? resistances = null,
            AdditionalEffectMetadataInvokeEffect? invoke = null, bool invincible = false, AdditionalEffectMetadataProperty? property = null,
            Dictionary<BasicAttribute, float>? rates = null) {
        var status = new AdditionalEffectMetadataStatus(values ?? new Dictionary<BasicAttribute, long>(), rates ?? new Dictionary<BasicAttribute, float>(),
            new Dictionary<SpecialAttribute, float>(), specialRates ?? new Dictionary<SpecialAttribute, float>(),
            resistances ?? new Dictionary<BasicAttribute, float>(), false, null, null, 0, invincible);
        return new AdditionalEffectMetadata(id, 1, null!, property ?? Property, null!, null!, null!, status, null, null!, null, invoke, [], [], []);
    }

    // A held buff as the field keeps it, with only what the sums read: its data, stack count and enabled flag.
    internal static Buff Held(AdditionalEffectMetadata metadata, bool enabled, int stacks = 1) {
        var buff = (Buff) RuntimeHelpers.GetUninitializedObject(typeof(Buff));
        typeof(Buff).GetField(nameof(Buff.Metadata))!.SetValue(buff, metadata);
        typeof(Buff).GetField("<Stacks>k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(buff, stacks);
        typeof(Buff).GetField("<Enabled>k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(buff, enabled);
        return buff;
    }

    private static Stats Player() => new(new Dictionary<BasicAttribute, long> { { BasicAttribute.PhysicalRes, 100 } }, 0);

    [Test]
    public void ADisabledBuffGivesNoStats() {
        // 10100232: physical and magical res +150 while HP is at most 30 %; 10100231: offensive damage +10 % at 70 % and more.
        AdditionalEffectMetadata low = Effect(10100232, values: new Dictionary<BasicAttribute, long> { { BasicAttribute.PhysicalRes, 150 } });
        AdditionalEffectMetadata high = Effect(10100231, specialRates: new Dictionary<SpecialAttribute, float> { { SpecialAttribute.OffensivePhysicalDamage, 0.1f } });

        Stats stats = Player();
        StatsManager.AddBuffs(stats, [Held(low, false), Held(high, true)]);
        stats.Total();
        Assert.That(stats[BasicAttribute.PhysicalRes].Total, Is.EqualTo(100));
        Assert.That(stats[SpecialAttribute.OffensivePhysicalDamage].Total, Is.EqualTo(100));

        // The HP drops under 30 %: the bands swap.
        stats = Player();
        StatsManager.AddBuffs(stats, [Held(low, true), Held(high, false)]);
        stats.Total();
        Assert.That(stats[BasicAttribute.PhysicalRes].Total, Is.EqualTo(250));
        Assert.That(stats[SpecialAttribute.OffensivePhysicalDamage].Total, Is.EqualTo(0));
    }

    [Test]
    public void ADisabledBuffGivesNoInvokeDamageBonus() {
        // 90050806 adds 20 thousandths to 90050805's physical damage; held disabled, it adds nothing.
        var invoke = new AdditionalEffectMetadataInvokeEffect([InvokeEffectType.IncreaseOffensivePhysicalDamage], [20f], [0f], EffectId: 90050805, EffectGroupId: 0,
            SkillId: 0, SkillGroupId: 0);
        AdditionalEffectMetadata named = Effect(90050805);
        AdditionalEffectMetadata bonus = Effect(90050806, invoke: invoke);

        Stats on = Player();
        StatsManager.AddBuffs(on, [Held(named, true), Held(bonus, true)]);
        on.Total();
        Assert.That(on[SpecialAttribute.OffensivePhysicalDamage].Total, Is.EqualTo(20));

        Stats off = Player();
        StatsManager.AddBuffs(off, [Held(named, true), Held(bonus, false)]);
        off.Total();
        Assert.That(off[SpecialAttribute.OffensivePhysicalDamage].Total, Is.EqualTo(0));
    }

    [Test]
    public void ADisabledBuffIsNotInTheSums() {
        var invoke = new AdditionalEffectMetadataInvokeEffect([InvokeEffectType.IncreaseSkillDamage], [0f], [0.1f], EffectId: 0, EffectGroupId: 0,
            SkillId: 10500001, SkillGroupId: 0);
        AdditionalEffectMetadataProperty stun = Property with { Stun = 1 };
        AdditionalEffectMetadata resist = Effect(1, resistances: new Dictionary<BasicAttribute, float> { { BasicAttribute.Accuracy, 0.1f } });
        AdditionalEffectMetadata invoking = Effect(2, invoke: invoke);
        AdditionalEffectMetadata invincible = Effect(3, invincible: true);
        AdditionalEffectMetadata stunning = Effect(4, property: stun);

        BuffSums off = BuffManager.SumsOf([Held(resist, false, 2), Held(invoking, false), Held(invincible, false), Held(stunning, false)]);
        Assert.That(off.Resistance(BasicAttribute.Accuracy), Is.EqualTo(0f));
        Assert.That(off.Invokes(InvokeEffectType.IncreaseSkillDamage, 10500001), Is.EqualTo((0, 0f)));
        Assert.That(off.Invincible, Is.False);
        Assert.That(off.Stun, Is.False);

        BuffSums on = BuffManager.SumsOf([Held(resist, true, 2), Held(invoking, true), Held(invincible, true), Held(stunning, true)]);
        Assert.That(on.Resistance(BasicAttribute.Accuracy), Is.EqualTo(0.2f).Within(1e-6f));
        Assert.That(on.Invokes(InvokeEffectType.IncreaseSkillDamage, 10500001).Item2, Is.EqualTo(0.1f).Within(1e-6f));
        Assert.That(on.Invincible, Is.True);
        Assert.That(on.Stun, Is.True);
    }
}
