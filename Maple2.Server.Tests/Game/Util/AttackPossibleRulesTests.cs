using System.Collections.Generic;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

// attackPossibleSkillIDs, attackPossibleDotEffectIDs and attackPossibleEffectID: the holder takes only the hits named.
public class AttackPossibleRulesTests {
    // 62100016: skill 90003005 and dot 120030007 break the shield.
    private static readonly AttackPossibleMetadata Shield = new([90003005], [120030007], 0);
    // 50004543: only holders of 90000874 hit it.
    private static readonly AttackPossibleMetadata ByEffect = new([], [], 90000874);

    private static bool HoldsNone(int effectId) => false;
    private static bool HoldsCream(int effectId) => effectId == 90000874;

    [Test]
    public void NamedSkillAndDotHit() {
        Assert.That(AttackPossibleRules.Allows(Shield, 90003005, 0, HoldsNone), Is.True);
        Assert.That(AttackPossibleRules.Allows(Shield, 0, 120030007, HoldsNone), Is.True);
        Assert.That(AttackPossibleRules.Allows(Shield, 10000001, 0, HoldsNone), Is.False);
        Assert.That(AttackPossibleRules.Allows(Shield, 0, 120030008, HoldsNone), Is.False);
        // A skill id is not a dot id: the dot list does not let the skill 120030007 through.
        Assert.That(AttackPossibleRules.Allows(Shield, 120030007, 0, HoldsNone), Is.False);
    }

    [Test]
    public void EffectHolderHitsWithAnything() {
        Assert.That(AttackPossibleRules.Allows(ByEffect, 10000001, 0, HoldsCream), Is.True);
        Assert.That(AttackPossibleRules.Allows(ByEffect, 0, 120030008, HoldsCream), Is.True);
        Assert.That(AttackPossibleRules.Allows(ByEffect, 10000001, 0, HoldsNone), Is.False);
    }

    [Test]
    public void EachLimitHeldMustAllow() {
        Assert.That(AttackPossibleRules.Allows([Shield, ByEffect], 90003005, 0, HoldsNone), Is.False);
        Assert.That(AttackPossibleRules.Allows([Shield, ByEffect], 90003005, 0, HoldsCream), Is.True);
        Assert.That(AttackPossibleRules.Allows([], 10000001, 0, HoldsNone), Is.True);
    }

    private static AdditionalEffectMetadata Effect(int id, AttackPossibleMetadata? limit) {
        var property = new AdditionalEffectMetadataProperty(BuffType.Buff, BuffSubType.Status, BuffCategory.None, BuffEventType.None,
            0, 15000, 0, 0, 1, false, false, false, false, false, false, false, 0, 0, 0, 0, 0, BuffKeepCondition.TimerDuration, default, default, 0,
            AttackPossible: limit);
        var status = new AdditionalEffectMetadataStatus(new Dictionary<BasicAttribute, long>(), new Dictionary<BasicAttribute, float>(),
            new Dictionary<SpecialAttribute, float>(), new Dictionary<SpecialAttribute, float>(),
            new Dictionary<BasicAttribute, float>(), false, null, null, 0, false, false);
        return new AdditionalEffectMetadata(id, 1, null!, property, null!, null!, null!, status, null, null!, null, null, [], [], []);
    }

    [Test]
    public void SumsRefuseOnlyForHolders() {
        var none = new BuffSums([(Effect(1, null), 1)]);
        Assert.That(none.AttackRefused(10000001, 0, HoldsNone), Is.False);
        var held = new BuffSums([(Effect(1, null), 1), (Effect(62100016, Shield), 1)]);
        Assert.That(held.AttackRefused(10000001, 0, HoldsNone), Is.True);
        Assert.That(held.AttackRefused(90003005, 0, HoldsNone), Is.False);
        Assert.That(held.AttackRefused(0, 120030007, HoldsNone), Is.False);
    }
}
