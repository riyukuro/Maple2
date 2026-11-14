using System.Collections.Generic;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model.Skill;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game;

// ReflectRecord.Share on the KR reflect levels: the hit times the reflect rate of its attack type, less the
// reflectionDamageReduceRate part, rounded.
public class ReflectShareTests {
    private static AdditionalEffectMetadataReflect Reflect(float physical, float magical, float reduce) {
        var rates = new Dictionary<BasicAttribute, float>();
        if (physical > 0) {
            rates[BasicAttribute.PhysicalAtk] = physical;
        }
        if (magical > 0) {
            rates[BasicAttribute.MagicalAtk] = magical;
        }
        return new AdditionalEffectMetadataReflect(1f, 1, 1, 99999, 0, 0, new Dictionary<BasicAttribute, long>(), rates, reduce);
    }

    // 60131001 (rates 0.1, reduce 0.9): 1 % of the hit. 49112001 (rates 0.01, reduce 0.5). 49126002 (rates 0.02, reduce 0.2).
    [TestCase(0.1f, 0.9f, 7452, 75)]
    [TestCase(0.01f, 0.5f, 10000, 50)]
    [TestCase(0.02f, 0.2f, 1000, 16)]
    public void Share_IsTheRateLessTheReduceRate(float rate, float reduce, long hit, long share) {
        Assert.That(ReflectRecord.Share(Reflect(rate, rate, reduce), AttackType.Physical, hit), Is.EqualTo(share));
        Assert.That(ReflectRecord.Share(Reflect(rate, rate, reduce), AttackType.Magic, hit), Is.EqualTo(share));
    }

    // The attack type picks the rate: a physical hit takes the physical rate only, an attack of both the larger one,
    // an attack of none (and a hit that took nothing) none.
    [Test]
    public void Share_ByTheAttackType() {
        AdditionalEffectMetadataReflect physicalOnly = Reflect(0.1f, 0, 0);
        Assert.That(ReflectRecord.Share(physicalOnly, AttackType.Physical, 1000), Is.EqualTo(100));
        Assert.That(ReflectRecord.Share(physicalOnly, AttackType.Magic, 1000), Is.EqualTo(0));
        Assert.That(ReflectRecord.Share(physicalOnly, AttackType.All, 1000), Is.EqualTo(100));
        Assert.That(ReflectRecord.Share(physicalOnly, AttackType.None, 1000), Is.EqualTo(0));
        Assert.That(ReflectRecord.Share(physicalOnly, AttackType.Physical, 0), Is.EqualTo(0));
    }

    // 50003300 (no reflect rate): the effect keeps its own dot (50003301: 0.11 of the target's max health).
    [Test]
    public void HasShare_OnlyWithARate() {
        Assert.That(ReflectRecord.HasShare(Reflect(0, 0, 0)), Is.False);
        Assert.That(ReflectRecord.HasShare(Reflect(0.02f, 0.02f, 0.2f)), Is.True);
    }
}
