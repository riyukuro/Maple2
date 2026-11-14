using System.Numerics;
using Maple2.Model.Enum;
using Maple2.Model.Metadata.FieldEntity;
using Maple2.Server.Game.Util;
using Maple2.Tools.VectorMath;

namespace Maple2.Server.Tests.Game;

// The client keeps a region hit of an attack with rayPhysxTest only when a PhysX ray from the caster to it, flattened
// to the caster's height, is not blocked (magicdata.cpp:1781-1799). The server logs the hits a map box collider stands
// in front of (SkillHitRules.SightBlocker). Made-up boxes: the caster stands at the origin on a floor at z 0.
public class SightBlockerTests {
    private static readonly FieldEntityId Id = new(0, 0, string.Empty);

    private static FieldBoxColliderEntity Box(Vector3 min, Vector3 max, bool fluid = false, float rotationZ = 0) {
        return new FieldBoxColliderEntity(Id, 0.5f * (min + max), new Vector3(0, 0, rotationZ), 1f, new BoundingBox3(min, max), max - min, false, fluid, MapAttribute.grass);
    }

    private static readonly Vector3 Caster = new(0, 0, 0);
    private static readonly Vector3 Target = new(600, 0, 0);
    private static readonly FieldBoxColliderEntity Floor = Box(new Vector3(-1000, -1000, -150), new Vector3(1000, 1000, 0));
    private static readonly FieldBoxColliderEntity Wall = Box(new Vector3(250, -500, -150), new Vector3(350, 500, 300));

    [Test]
    public void AWallBetween_Blocks() {
        Assert.That(SkillHitRules.SightBlocker(Caster, Target, [Floor, Wall]), Is.SameAs(Wall));
    }

    [Test]
    public void TheFloorAndAStepBelowTheMargin_DoNotBlock() {
        FieldBoxColliderEntity step = Box(new Vector3(250, -500, -150), new Vector3(350, 500, 15));
        Assert.That(SkillHitRules.SightBlocker(Caster, Target, [Floor, step]), Is.Null);
    }

    [Test]
    public void AFluidOrTurnedBox_DoesNotBlock() {
        Assert.Multiple(() => {
            Assert.That(SkillHitRules.SightBlocker(Caster, Target, [Box(Wall.Bounds.Min, Wall.Bounds.Max, fluid: true)]), Is.Null);
            Assert.That(SkillHitRules.SightBlocker(Caster, Target, [Box(Wall.Bounds.Min, Wall.Bounds.Max, rotationZ: 45)]), Is.Null);
            Assert.That(SkillHitRules.SightBlocker(Caster, Target, [Box(Wall.Bounds.Min, Wall.Bounds.Max, rotationZ: 270)]), Is.Not.Null, "a quarter turn keeps the box");
        });
    }

    [Test]
    public void ABoxHoldingTheCaster_OrPastTheTarget_DoesNotBlock() {
        FieldBoxColliderEntity around = Box(new Vector3(-100, -100, -150), new Vector3(100, 100, 300));
        FieldBoxColliderEntity past = Box(new Vector3(650, -500, -150), new Vector3(750, 500, 300));
        Assert.That(SkillHitRules.SightBlocker(Caster, Target, [around, past]), Is.Null);
    }

    // The ray runs at the caster's height: a wall the target stands on top of, or one above the caster, is not in it.
    [Test]
    public void TheRayRunsAtTheCastersHeight() {
        FieldBoxColliderEntity high = Box(new Vector3(250, -500, 100), new Vector3(350, 500, 400));
        Assert.Multiple(() => {
            Assert.That(SkillHitRules.SightBlocker(Caster, Target, [high]), Is.Null, "above the caster");
            Assert.That(SkillHitRules.SightBlocker(Caster, Target with { Z = 450 }, [high]), Is.Null, "a target up high");
            Assert.That(SkillHitRules.SightBlocker(Caster with { Z = 150 }, Target, [high]), Is.SameAs(high), "a caster up high");
        });
    }

    [Test]
    public void ARayAlongAnEdge_OrStraightUp_DoesNotBlock() {
        Assert.Multiple(() => {
            Assert.That(SkillHitRules.SightBlocker(Caster, new Vector3(600, 500, 0), [Box(new Vector3(250, 500, -150), new Vector3(350, 900, 300))]), Is.Null);
            Assert.That(SkillHitRules.SightBlocker(Caster, new Vector3(0, 0, 500), [Wall]), Is.Null);
        });
    }

    [Test]
    public void AFieldWithoutMapData_LogsNothing() {
        Assert.That(SkillHitRules.SightBlocker(null, Caster, Target), Is.Null);
    }
}
