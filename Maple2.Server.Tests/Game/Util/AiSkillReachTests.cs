using System.Numerics;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

// The reach of a skill for the AI's trace and skillRange, as the client reads a skill's reach (PCBase::vfunc_101).
public class AiSkillReachTests {
    private static SkillMetadataRange Range(SkillRegion type, float distance, float endWidth = 0) =>
        new(type, distance, 0, 0, endWidth, 0, Vector3.Zero, Vector3.Zero, default, default, default);

    [Test]
    public void RadiusIsTheAttackDistance() {
        // 50000381 (23000019): box, distance 1200, no autoTargeting distance and a detectProperty distance of 0.
        Assert.That(AiSkillReach.Radius(0, Range(SkillRegion.Box, 1200)), Is.EqualTo(1200));
        Assert.That(AiSkillReach.Radius(0, Range(SkillRegion.Cylinder, 900)), Is.EqualTo(900));
    }

    [Test]
    public void HoleCylinderUsesEndWidth() {
        Assert.That(AiSkillReach.Radius(0, Range(SkillRegion.HoleCylinder, 300, 800)), Is.EqualTo(800));
    }

    [Test]
    public void AutoTargetingDistanceComesFirst() {
        Assert.That(AiSkillReach.Radius(450, Range(SkillRegion.Box, 1200)), Is.EqualTo(450));
    }

    [Test]
    public void NoAttackHasNoReach() {
        Assert.That(AiSkillReach.Radius(0, null), Is.EqualTo(0));
    }

    [Test]
    public void InReachIsAFlatCircleWithAHeight() {
        Vector3 caster = new(0, 0, 100);
        Assert.Multiple(() => {
            Assert.That(AiSkillReach.InReach(caster, new Vector3(1200, 0, 100), 1200, 150), Is.True);
            Assert.That(AiSkillReach.InReach(caster, new Vector3(1200 + AiSkillReach.Slack, 0, 100), 1200, 150), Is.True);
            Assert.That(AiSkillReach.InReach(caster, new Vector3(1300, 0, 100), 1200, 150), Is.False);
            // The old check: a 0 detect distance passed only within 10 units.
            Assert.That(AiSkillReach.InReach(caster, new Vector3(600, 600, 100), 1200, 150), Is.True);
            Assert.That(AiSkillReach.InReach(caster, new Vector3(0, 0, 100 + 150), 1200, 150), Is.True);
            Assert.That(AiSkillReach.InReach(caster, new Vector3(0, 0, 100 - 151), 1200, 150), Is.False);
        });
    }
}
