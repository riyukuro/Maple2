using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Model;

namespace Maple2.Server.Tests.Game.Util;

// A stacked buff's values: status values and rates, dot and recovery amounts at 1 stack, 2 stacks and the maximum.
public class BuffStackTests {
    [Test]
    public void FactorIsTheCount() {
        Assert.That(Buff.StackFactor(1), Is.EqualTo(1));
        Assert.That(Buff.StackFactor(2), Is.EqualTo(2));
        Assert.That(Buff.StackFactor(10), Is.EqualTo(10));
        // A buff without a stack maximum has count 0: its status and invoke values count 0 times, as in the client,
        // and its dot or recovery amount once.
        Assert.That(Buff.StackFactor(0), Is.EqualTo(0));
        Assert.That(Buff.ByStacks(12L, 0), Is.EqualTo(0));
        Assert.That(Buff.AmountFactor(0), Is.EqualTo(1));
        Assert.That(Buff.AmountByStacks(150, 0), Is.EqualTo(150));
    }

    // A stat refresh applies a worn item's effects again: one already held is skipped, at its level or above.
    [Test]
    public void ItemEffectHeldIsNotAppliedAgain() {
        ItemMetadataAdditionalEffect[] effects = [new(90050809, 1, false), new(90050816, 1, false), new(90050820, 2, false)];
        var heldLevels = new System.Collections.Generic.Dictionary<int, short> { [90050809] = 1, [90050820] = 1 };
        bool Held(int id, short level) => heldLevels.TryGetValue(id, out short have) && have >= level;

        Assert.That(BuffManager.ItemEffectsToAdd(effects, Held).Select(effect => effect.Id), Is.EqualTo(new[] { 90050816, 90050820 }));
        // Applied once, a second refresh adds nothing.
        heldLevels[90050816] = 1;
        heldLevels[90050820] = 2;
        Assert.That(BuffManager.ItemEffectsToAdd(effects, Held), Is.Empty);
    }

    // Invoke values are summed over the buffs held: times the stack count, the same buff from two casters twice,
    // only the asked type of a buff with two types, and only for the buff's skill or skill group.
    [Test]
    public void InvokeValuesSumOverHeldBuffs() {
        var bySkill = new AdditionalEffectMetadataInvokeEffect([InvokeEffectType.IncreaseSkillDamage, InvokeEffectType.IncreaseEffectDuration],
            [0f, 500f], [0.1f, 0f], 0, 0, SkillId: 10500001, SkillGroupId: 0);
        var byGroup = new AdditionalEffectMetadataInvokeEffect([InvokeEffectType.IncreaseSkillDamage], [0f], [0.05f], 0, 0, SkillId: 0, SkillGroupId: 7);

        (int value, float rate) = BuffManager.SumInvokes([(bySkill, 3), (bySkill, 1), (byGroup, 2), (null, 1)],
            InvokeEffectType.IncreaseSkillDamage, 10500001, 7);
        Assert.That(value, Is.EqualTo(0));
        Assert.That(rate, Is.EqualTo(0.5f).Within(1e-6f));

        (value, rate) = BuffManager.SumInvokes([(bySkill, 3), (bySkill, 1)], InvokeEffectType.IncreaseEffectDuration, 10500001);
        Assert.That(value, Is.EqualTo(2000));
        Assert.That(rate, Is.EqualTo(0f));

        // Another skill, and a caster's buff removed.
        Assert.That(BuffManager.SumInvokes([(bySkill, 3)], InvokeEffectType.IncreaseSkillDamage, 10500002), Is.EqualTo((0, 0f)));
        Assert.That(BuffManager.SumInvokes([(bySkill, 1)], InvokeEffectType.IncreaseSkillDamage, 10500001).Item2, Is.EqualTo(0.1f).Within(1e-6f));
    }

    // The counts a buff with the maximum reaches (Buff.AddStacks) and its status value at each.
    [TestCase(1, 5, 1, 12)]
    [TestCase(1, 5, 2, 24)]
    [TestCase(1, 5, 5, 60)]
    [TestCase(1, 5, 9, 60)] // more applications than the maximum stay at the maximum
    public void StatusValueByStacks(int first, int max, int applications, long expected) {
        int stacks = Buff.AddStacks(first, max);
        for (int i = 1; i < applications; i++) {
            stacks = System.Math.Min(max, stacks + Buff.AddStacks(1, max));
        }
        Assert.That(Buff.ByStacks(12L, stacks), Is.EqualTo(expected));
    }

    [TestCase(1, 0.03f)]
    [TestCase(2, 0.06f)]
    [TestCase(6, 0.18f)]
    public void StatusRateByStacks(int stacks, float expected) {
        Assert.That(Buff.ByStacks(0.03f, stacks), Is.EqualTo(expected).Within(1e-6f));
    }

    // A dot of 0.8 % of the target's health with a maximum of 3 stacks, on 83,258,250 health: KMS2 hits for
    // 666,066 at 1 stack and 1,998,198 at 3 (to the rounding of the health).
    [TestCase(1, 666_066)]
    [TestCase(2, 1_332_132)]
    [TestCase(3, 1_998_198)]
    public void DotAmountByStacks(int stacks, int expected) {
        long one = (long) (0.008 * 83_258_250);
        Assert.That(Buff.AmountByStacks(one, stacks), Is.EqualTo(expected));
    }

    [TestCase(1, 150)]
    [TestCase(2, 300)]
    [TestCase(4, 600)]
    public void RecoveryAmountByStacks(int stacks, int expected) {
        Assert.That(Buff.AmountByStacks(150, stacks), Is.EqualTo(expected));
    }

    [Test]
    public void AmountStaysInsideTheInt() {
        Assert.That(Buff.AmountByStacks(2_000_000_000, 3), Is.EqualTo(int.MaxValue));
    }
}
