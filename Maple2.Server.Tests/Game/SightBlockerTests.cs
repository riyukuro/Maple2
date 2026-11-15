using System.Collections.Generic;
using System.Numerics;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Game.Field;
using Maple2.Model.Metadata.FieldEntity;
using Maple2.Server.Game.Util;
using Maple2.Tools.VectorMath;

namespace Maple2.Server.Tests.Game;

// The client keeps a region hit of an attack with rayPhysxTest only when a PhysX ray from the caster to it, flattened
// to the caster's height, is not blocked (magicdata.cpp:1781-1799). The server logs the hits a map box collider stands
// in front of (SkillHitRules.SightBlocker). Made-up boxes: the caster stands at the origin on a floor at z 0.
public class SightBlockerTests {
    private static readonly FieldEntityId Id = new(0, 0, string.Empty);

    private static FieldBoxColliderEntity Box(Vector3 min, Vector3 max, bool fluid = false, float rotationZ = 0, ushort group = 9) {
        return new FieldBoxColliderEntity(Id, 0.5f * (min + max), new Vector3(0, 0, rotationZ), 1f, new BoundingBox3(min, max), max - min, false, fluid, MapAttribute.grass, group);
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

    // The ray tests PhysX groups 0 and 9 (0x201): a whitebox of group 7 (most of them) or a cube of group 8 is passed.
    [Test]
    public void ABoxOutsideTheRaysGroups_DoesNotBlock() {
        Assert.Multiple(() => {
            Assert.That(SkillHitRules.SightBlocker(Caster, Target, [Box(Wall.Bounds.Min, Wall.Bounds.Max, group: 7)]), Is.Null);
            Assert.That(SkillHitRules.SightBlocker(Caster, Target, [Box(Wall.Bounds.Min, Wall.Bounds.Max, group: 8)]), Is.Null);
            Assert.That(SkillHitRules.SightBlocker(Caster, Target, [Box(Wall.Bounds.Min, Wall.Bounds.Max, group: 3)]), Is.Null);
            Assert.That(SkillHitRules.SightBlocker(Caster, Target, [Box(Wall.Bounds.Min, Wall.Bounds.Max, group: 0)]), Is.Not.Null, "group 0");
            Assert.That(SkillHitRules.SightBlocker(Caster, Target, [Box(Wall.Bounds.Min, Wall.Bounds.Max, group: 9)]), Is.Not.Null, "group 9");
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

    // A ray only along a box's edge passes; a target straight above or below has no ray (SightTest drops it).
    [Test]
    public void ARayAlongAnEdge_OrNoRayAtAll_HasNoBlocker() {
        Assert.Multiple(() => {
            Assert.That(SkillHitRules.SightBlocker(Caster, new Vector3(600, 500, 0), [Box(new Vector3(250, 500, -150), new Vector3(350, 900, 300))]), Is.Null);
            Assert.That(SkillHitRules.SightBlocker(Caster, new Vector3(0, 0, 500), [Wall]), Is.Null);
        });
    }

    // The client drops a hit whose point is straight above or below the owner (flattened length 0, magicdata.cpp:1793).
    // A target with a circle there holds the owner, and its nearest part may stand off that line: not counted.
    [Test]
    public void ATargetStraightAboveTheOwner_IsDroppedOverhead() {
        Assert.Multiple(() => {
            Assert.That(SkillHitRules.SightTest(Caster, new Vector3(0, 0, 500), 0, [], out FieldBoxColliderEntity? blocker), Is.EqualTo(SkillHitRules.SightDrop.Overhead));
            Assert.That(blocker, Is.Null);
            Assert.That(SkillHitRules.SightTest(Caster, new Vector3(0, 0, 500), 50, [], out _), Is.EqualTo(SkillHitRules.SightDrop.None));
        });
    }

    // The client aims at the target's nearest collision part, which the server lacks: a hit is blocked only when the ray
    // to its position and the ray to the point of its circle nearest the owner are both blocked.
    [Test]
    public void ATargetWhoseNearSideIsInSight_IsNotBlocked() {
        // A big target standing in the wall (x 250-350): its centre at x 400 is behind it, its side at x 240 is not.
        var big = new Vector3(400, 0, 0);
        var target = new Vector3(600, 0, 0);
        Assert.Multiple(() => {
            Assert.That(SkillHitRules.SightTest(Caster, big, 0, [Wall], out _), Is.EqualTo(SkillHitRules.SightDrop.Blocked), "no radius: the position decides");
            Assert.That(SkillHitRules.SightTest(Caster, big, 160, [Wall], out _), Is.EqualTo(SkillHitRules.SightDrop.None), "the near side is in sight");
            Assert.That(SkillHitRules.SightTest(Caster, target, 50, [Wall], out FieldBoxColliderEntity? blocker), Is.EqualTo(SkillHitRules.SightDrop.Blocked), "both blocked");
            Assert.That(blocker, Is.SameAs(Wall));
            Assert.That(SkillHitRules.SightTest(new Vector3(560, 0, 0), target, 50, [Wall], out _), Is.EqualTo(SkillHitRules.SightDrop.None), "the owner stands in the target's circle");
        });
    }

    [Test]
    public void AFieldWithoutMapData_HasNoBoxes() {
        Assert.Multiple(() => {
            Assert.That(SkillHitRules.SightBoxes(null, Caster, [(Target, 50f)]), Is.Empty);
            Assert.That(SkillHitRules.StepBoxes(null, Caster, Target), Is.Empty);
        });
    }

    // A point that is not finite passes the reach check (a NaN distance is never too far): it queries no cells.
    [Test]
    public void AStepWithANaNPoint_QueriesNoBoxes() {
        FieldBoxColliderEntity cube = Box(new Vector3(-75, -75, 0), new Vector3(75, 75, 150));
        var map = new FieldAccelerationStructure();
        map.AddEntities(new Dictionary<Vector3S, List<FieldEntity>> { [new Vector3S(0, 0, 0)] = [cube] }, new Vector3S(0, 0, 0), new Vector3S(4, 4, 4), [], 0);
        var nan = new Vector3(float.NaN, 0, 0);
        Assert.Multiple(() => {
            Assert.That(SkillHitRules.StepBoxes(map, Caster, new Vector3(10, 0, 0)), Does.Contain(cube), "a finite step finds the cube");
            Assert.That(SkillHitRules.StepBoxes(map, Caster, nan), Is.Empty, "current");
            Assert.That(SkillHitRules.StepBoxes(map, nan, Caster), Is.Empty, "last");
            Assert.That(SkillHitRules.StepBoxes(map, Caster, new Vector3(float.PositiveInfinity, 0, 0)), Is.Empty, "infinite");
        });
    }

    // The client stops an arrow with rayPhysxTest 5 short of a box its ray tests (magicdata.cpp:1011-1019), so a
    // frame's step never enters one.
    [Test]
    public void AnArrowStepThroughAWall_IsFound() {
        var from = new Vector3(0, 0, 50);
        Assert.Multiple(() => {
            Assert.That(SkillHitRules.StepBlocker(from, new Vector3(600, 0, 50), [Floor, Wall]), Is.SameAs(Wall), "through");
            Assert.That(SkillHitRules.StepBlocker(from, new Vector3(245, 0, 50), [Floor, Wall]), Is.Null, "stopped 5 short");
            Assert.That(SkillHitRules.StepBlocker(new Vector3(400, 0, 50), new Vector3(600, 0, 50), [Floor, Wall]), Is.Null, "past it");
            Assert.That(SkillHitRules.StepBlocker(new Vector3(0, 0, 400), new Vector3(600, 0, 400), [Floor, Wall]), Is.Null, "over it");
        });
    }

    [Test]
    public void AnArrowStep_IntoTheFloorPastTheMargin_IsFound() {
        var from = new Vector3(0, 0, 50);
        Assert.Multiple(() => {
            Assert.That(SkillHitRules.StepBlocker(from, new Vector3(100, 0, 0), [Floor]), Is.Null, "onto the floor");
            Assert.That(SkillHitRules.StepBlocker(from, new Vector3(100, 0, -1), [Floor]), Is.Null, "within the margin");
            Assert.That(SkillHitRules.StepBlocker(from, new Vector3(100, 0, -10), [Floor]), Is.SameAs(Floor), "into it");
        });
    }

    [Test]
    public void AnArrowStep_ThroughABoxTheRayPasses_OrFromInsideOne_IsNotFound() {
        var from = new Vector3(0, 0, 50);
        var to = new Vector3(600, 0, 50);
        Assert.Multiple(() => {
            Assert.That(SkillHitRules.StepBlocker(from, to, [Box(Wall.Bounds.Min, Wall.Bounds.Max, group: 7)]), Is.Null, "group 7");
            Assert.That(SkillHitRules.StepBlocker(from, to, [Box(Wall.Bounds.Min, Wall.Bounds.Max, fluid: true)]), Is.Null, "fluid");
            Assert.That(SkillHitRules.StepBlocker(from, to, [Box(Wall.Bounds.Min, Wall.Bounds.Max, rotationZ: 45)]), Is.Null, "turned");
            Assert.That(SkillHitRules.StepBlocker(new Vector3(300, 0, 50), to, [Wall]), Is.Null, "from inside");
            Assert.That(SkillHitRules.StepBlocker(from, from, [Wall]), Is.Null, "no step");
        });
    }
}
