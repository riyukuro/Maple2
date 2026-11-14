using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Model;

namespace Maple2.Server.Tests.Game.Util;

// The kept sums of the buffs held (BuffSums) equal a walk over the same buffs.
public class BuffSumsTests {
    private static AdditionalEffectMetadata Effect(int id, Dictionary<BasicAttribute, float>? resistances = null,
            AdditionalEffectMetadataInvokeEffect? invoke = null, bool invincible = false, bool reflect = false, int stun = 0) {
        AdditionalEffectMetadataProperty property = stun == 0 ? null! : new AdditionalEffectMetadataProperty(BuffType.Debuff, BuffSubType.Status, BuffCategory.None, BuffEventType.None,
            0, 1000, 0, 0, 1, false, false, false, false, false, false, false, 0, 0, 0, 0, stun, BuffKeepCondition.TimerDuration, default, default, 0);
        var status = new AdditionalEffectMetadataStatus(new Dictionary<BasicAttribute, long>(), new Dictionary<BasicAttribute, float>(),
            new Dictionary<SpecialAttribute, float>(), new Dictionary<SpecialAttribute, float>(),
            resistances ?? new Dictionary<BasicAttribute, float>(), false, null, null, 0, invincible, reflect);
        return new AdditionalEffectMetadata(id, 1, null!, property, null!, null!, null!, status, null, null!, null, invoke, [], [], []);
    }

    private static readonly AdditionalEffectMetadataInvokeEffect BySkill = new(
        [InvokeEffectType.IncreaseSkillDamage, InvokeEffectType.IncreaseEffectDuration], [0f, 500f], [0.1f, 0f], 0, 0, SkillId: 10500001, SkillGroupId: 0);
    private static readonly AdditionalEffectMetadataInvokeEffect ByGroup = new(
        [InvokeEffectType.IncreaseSkillDamage], [0f], [0.05f], 0, 0, SkillId: 0, SkillGroupId: 7);

    private static readonly (AdditionalEffectMetadata Metadata, int Stacks)[] Held = [
        (Effect(1, new Dictionary<BasicAttribute, float> { [BasicAttribute.Accuracy] = 0.1f, [BasicAttribute.Piercing] = -0.2f }), 1),
        (Effect(2, new Dictionary<BasicAttribute, float> { [BasicAttribute.Accuracy] = 0.05f }, BySkill), 3),
        (Effect(2, new Dictionary<BasicAttribute, float> { [BasicAttribute.Accuracy] = 0.05f }, BySkill), 1),
        (Effect(3, invoke: ByGroup), 2),
        (Effect(4, new Dictionary<BasicAttribute, float> { [BasicAttribute.CriticalDamage] = 0.3f }), 0),
        (Effect(5), 1),
    ];

    private static float WalkResistance(IEnumerable<(AdditionalEffectMetadata Metadata, int Stacks)> held, BasicAttribute attribute) {
        float sum = 0;
        foreach ((AdditionalEffectMetadata metadata, int stacks) in held) {
            if (metadata.Status.Resistances.TryGetValue(attribute, out float value)) {
                sum += Buff.ByStacks(value, stacks);
            }
        }
        return sum;
    }

    [Test]
    public void ResistancesMatchTheWalk() {
        var sums = new BuffSums(Held);
        foreach (BasicAttribute attribute in System.Enum.GetValues<BasicAttribute>()) {
            Assert.That(sums.Resistance(attribute), Is.EqualTo(WalkResistance(Held, attribute)).Within(1e-6f), attribute.ToString());
        }
        Assert.That(sums.Resistance(BasicAttribute.Accuracy), Is.EqualTo(0.3f).Within(1e-6f));
        Assert.That(sums.Resistance(BasicAttribute.Piercing), Is.EqualTo(-0.2f).Within(1e-6f));
        // A buff with count 0 counts 0 times.
        Assert.That(sums.Resistance(BasicAttribute.CriticalDamage), Is.EqualTo(0f));
    }

    [Test]
    public void InvokesMatchTheWalk() {
        var sums = new BuffSums(Held);
        var walk = Held.Select(entry => (entry.Metadata.InvokeEffect, entry.Stacks)).ToArray();
        foreach (InvokeEffectType type in System.Enum.GetValues<InvokeEffectType>()) {
            foreach ((int skillId, int[] groups) in new[] { (10500001, new[] { 7 }), (10500001, System.Array.Empty<int>()), (10500002, new[] { 7 }), (0, new[] { 0 }) }) {
                Assert.That(sums.Invokes(type, skillId, groups), Is.EqualTo(BuffManager.SumInvokes(walk, type, skillId, groups)), $"{type} {skillId}");
            }
        }
        Assert.That(sums.Invokes(InvokeEffectType.IncreaseSkillDamage, 10500001, 7).Item2, Is.EqualTo(0.5f).Within(1e-6f));
        Assert.That(sums.Invokes(InvokeEffectType.IncreaseEffectDuration, 10500001).Item1, Is.EqualTo(2000));
    }

    // A suspended buff (its holder on a battle mount) keeps its stun and invoke parts and gives no resistance or
    // invincible flag (AdditionalCliList::FUN_140972a00 skips it; FUN_1409713b0 and CollectInvokeEffectStats do not).
    [Test]
    public void SuspendedBuffKeepsOnlyItsStunAndInvokeParts() {
        AdditionalEffectMetadata effect = Effect(7, new Dictionary<BasicAttribute, float> { [BasicAttribute.Accuracy] = 0.1f },
            BySkill, invincible: true, stun: 1);
        BuffSums held = BuffSums.Of([(effect, 1, false)]);
        BuffSums suspended = BuffSums.Of([(effect, 1, true)]);
        Assert.Multiple(() => {
            Assert.That(held.Invincible, Is.True);
            Assert.That(held.Resistance(BasicAttribute.Accuracy), Is.EqualTo(0.1f).Within(1e-6f));
            Assert.That(suspended.Invincible, Is.False, "invincible");
            Assert.That(suspended.Resistance(BasicAttribute.Accuracy), Is.EqualTo(0f), "resistance");
            Assert.That(suspended.Stun, Is.EqualTo(held.Stun), "stun");
            Assert.That(suspended.Invokes(InvokeEffectType.IncreaseSkillDamage, 10500001), Is.EqualTo(held.Invokes(InvokeEffectType.IncreaseSkillDamage, 10500001)), "invoke");
        });
    }

    [Test]
    public void InvincibleWhenOneBuffHasTheFlag() {
        Assert.That(new BuffSums(Held).Invincible, Is.False);
        Assert.That(new BuffSums([.. Held, (Effect(6, invincible: true), 1)]).Invincible, Is.True);
        Assert.That(new BuffSums([]).Invincible, Is.False);
        Assert.That((byte) DamageType.Invincible, Is.EqualTo(4));
    }

    [Test]
    public void StunWhenOneBuffHasAStunValueThatStuns() {
        Assert.That(new BuffSums(Held).Stun, Is.False);
        Assert.That(new BuffSums([.. Held, (Effect(6, stun: 1), 1)]).Stun, Is.True);
        Assert.That(new BuffSums([.. Held, (Effect(6, stun: 3), 1)]).Stun, Is.True);
        Assert.That(new BuffSums([.. Held, (Effect(6, stun: 2), 1)]).Stun, Is.False);
    }

    [Test]
    public void InvincibleReflectWhenAnInvincibleBuffHasValueTwo() {
        Assert.That(new BuffSums([.. Held, (Effect(6, invincible: true), 1)]).InvincibleReflect, Is.False);
        Assert.That(new BuffSums([.. Held, (Effect(6, invincible: true), 1), (Effect(7, invincible: true, reflect: true), 1)]).InvincibleReflect, Is.True);
        Assert.That(new BuffSums([.. Held, (Effect(7, reflect: true), 1)]).InvincibleReflect, Is.False);
        Assert.That((byte) DamageType.InvincibleReflection, Is.EqualTo(8));
    }

    [Test]
    public void NoBuffsIsZero() {
        var sums = new BuffSums([]);
        Assert.That(sums.Resistance(BasicAttribute.Accuracy), Is.EqualTo(0f));
        Assert.That(sums.Invokes(InvokeEffectType.IncreaseSkillDamage, 10500001), Is.EqualTo((0, 0f)));
    }
}
