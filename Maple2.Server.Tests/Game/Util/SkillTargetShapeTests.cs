using System.Collections.Generic;
using System.Numerics;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Util;
using Maple2.Tools.Collision;

namespace Maple2.Server.Tests.Game.Util;

// Where a target packet's objects may stand (Skill sub 1): the client's region writer (FUN_141464d00) searches the
// range at the owner's position turned to the owner's facing, its arrow writer (FUN_141465b30) the arrow's collision
// box over one step. The bounds are chosen here (SkillHitRules). Before, the server checked no object's place and no
// packet's facing or step.
public class SkillTargetShapeTests {
    private static SkillMetadataAttack Region(SkillRegion type, float distance, float width) {
        var range = new SkillMetadataRange(type, distance, 100, width, width, 0, default, default, IncludeCasterType.Exclude,
            ApplyTargetType.Hostile, SkillTargetType.None);
        var arrow = new SkillMetadataArrow(Overlap: false, Explosion: false, RayPhysXTest: false, NonTarget: SkillTargetType.Owner,
            BounceType: BounceType.None, BounceCount: 0, BounceRadius: 0, BounceOverlap: false,
            Collision: default, CollisionAdd: default, RayType: 0);
        var damage = new SkillMetadataDamage(1, 1, 0, 0, false, 0, 0, false, null);
        return new SkillMetadataAttack("Attack_01", 0, 5, 0, 0, 0, 0, [], null, range, arrow, damage, [], []);
    }

    private static SkillMetadataArrow Arrow(BounceType bounceType = BounceType.Pierce, int bounceCount = 1, float bounceRadius = 0) {
        return new SkillMetadataArrow(Overlap: false, Explosion: false, RayPhysXTest: false, NonTarget: SkillTargetType.Owner,
            BounceType: bounceType, BounceCount: bounceCount, BounceRadius: bounceRadius, BounceOverlap: false,
            Collision: new Vector3(50, 50, 50), CollisionAdd: default, RayType: 0);
    }

    private static MagicPath Path(float velocity, float distance) {
        return new MagicPath(Align: false, AlignHeight: 0, Rotate: true, IgnoreAdjust: false, ExplosionByDestroy: false,
            CatmullRom: false, IgnorePhysXTestInitPosition: false, IgnoreCancelAtSpawnTime: false,
            Direction: Vector3.UnitY, FireOffset: default, FireFixed: default, ControlValue0: default,
            ControlValue1: default, ControlEndOffsetValue: default, TraceTargetOffset: false,
            TraceTargetDuration: 0, Velocity: velocity, Distance: distance, RotateZDegree: 0, LifeTime: 0, DelayTime: 0,
            SpawnTime: 0, DestroyTime: 0, ControlRate: 0, LookAtType: 0, PiercingAttackInterval: 0,
            PiercingAttackMaxTargetCount: 0, NonTargetMoveDistance: 0, MoveEndHoldDuration: 0);
    }

    // A mob's shape: a circle of radius 25, 100 high, standing at x, y.
    private static Prism Mob(float x, float y, float z = 0) => new(new Circle(new Vector2(x, y), 25), z, 100);

    [Test]
    public void RegionAreaIsTheRangeAtThePlayerGrownByTheMargin() {
        // A cylinder of radius 300 at the player (1000, 1000): a mob 400 away is in (300 + 150 + its 25), one 500
        // away is not, nor one 400 away from where the client claims it stood.
        SkillMetadataAttack attack = Region(SkillRegion.Cylinder, 300, 0);
        var player = new Vector3(1000, 1000, 0);
        Prism area = SkillHitRules.RegionArea(attack, player, 0);
        Assert.That(area.Intersects(Mob(1400, 1000)), Is.True);
        Assert.That(area.Intersects(Mob(1000, 1500)), Is.False);
        Assert.That(SkillHitRules.RegionArea(attack, new Vector3(0, 0, 0), 0).Intersects(Mob(1400, 1000)), Is.False);
        Assert.That(area.Intersects(Mob(1400, 1000, 400)), Is.False, "far above");
    }

    [Test]
    public void RegionAreaTurnsWithTheFacing() {
        // A box 300 ahead and 150 wide: a mob 400 ahead is in, one 400 behind is not; turned around, the other way.
        SkillMetadataAttack attack = Region(SkillRegion.Box, 300, 150);
        Vector2 ahead = attack.GetPrism(Vector3.Zero, 0).Polygon.Contains(0, 200) ? Vector2.UnitY : -Vector2.UnitY;
        Prism area = SkillHitRules.RegionArea(attack, Vector3.Zero, 0);
        Assert.That(area.Intersects(Mob(ahead.X * 400, ahead.Y * 400)), Is.True);
        Assert.That(area.Intersects(Mob(-ahead.X * 400, -ahead.Y * 400)), Is.False);
        Prism turned = SkillHitRules.RegionArea(attack, Vector3.Zero, 180);
        Assert.That(turned.Intersects(Mob(-ahead.X * 400, -ahead.Y * 400)), Is.True);
        Assert.That(area.Intersects(Mob(400, 0)), Is.False, "beside it");
    }

    [Test]
    public void RegionFacingIsNearTheCastOrTheSyncedFacing() {
        Assert.That(SkillHitRules.FacingNear(80, 0, 0), Is.True, "the KMS2 bound is 79");
        Assert.That(SkillHitRules.FacingNear(350, 10, 10), Is.True, "across 0");
        Assert.That(SkillHitRules.FacingNear(179, 0, 0), Is.False);
        // 10200232 turns while cast: the facing strays from the cast's but stays near the synced one.
        Assert.That(SkillHitRules.FacingNear(179, 0, 150), Is.True);
        Assert.That(SkillHitRules.FacingNear(-90, 180, 180), Is.True, "negative degrees");
    }

    [Test]
    public void ArrowAreaIsTheStepsCollisionBox() {
        // A step from (0, 0) to (0, 100) with a 50 wide box, grown by 150.
        Prism area = SkillHitRules.ArrowArea(Arrow(), Vector3.Zero, new Vector3(0, 100, 0), Vector3.UnitY);
        Assert.That(area.Intersects(Mob(0, 150)), Is.True);
        Assert.That(area.Intersects(Mob(150, 50)), Is.True, "beside the step, inside the margin");
        Assert.That(area.Intersects(Mob(400, 50)), Is.False);
        Assert.That(area.Intersects(Mob(0, 600)), Is.False, "ahead of the step");
        // A step that does not move, with an upright axis: a box along y, not none.
        Prism still = SkillHitRules.ArrowArea(Arrow(), Vector3.Zero, Vector3.Zero, Vector3.UnitZ);
        Assert.That(still.Intersects(Mob(0, 50)), Is.True);
    }

    [Test]
    public void ArrowStepAndReachStayInsideThePaths() {
        MagicPath[] paths = [Path(500, 800)];
        float velocity = SkillHitRules.Velocity(paths);
        Assert.That(velocity, Is.EqualTo(500));
        // A step: at most 0.25 s of 500 and the margin, 275; KMS2 steps are at most 139.
        Assert.That(SkillHitRules.StepTooLong(Vector3.Zero, new Vector3(0, 139, 0), velocity), Is.False);
        Assert.That(SkillHitRules.StepTooLong(Vector3.Zero, new Vector3(0, 300, 0), velocity), Is.True);
        Assert.That(SkillHitRules.StepTooLong(Vector3.Zero, new Vector3(0, 3000, 0), 0), Is.False, "no path moves");
        // The reach: the path's 800 and the margin from the cast.
        Assert.That(SkillHitRules.ReachTooFar(new Vector3(0, 900, 0), Vector3.Zero, Arrow(), paths), Is.False);
        Assert.That(SkillHitRules.ReachTooFar(new Vector3(0, 1000, 0), Vector3.Zero, Arrow(), paths), Is.True);
        Assert.That(SkillHitRules.ReachTooFar(new Vector3(0, 1000, 0), Vector3.Zero, Arrow(), null), Is.False, "no path");
        // A chain of 2 links of radius 500 reaches 1000 further (10300071).
        Assert.That(SkillHitRules.ReachTooFar(new Vector3(0, 1900, 0), Vector3.Zero, Arrow(BounceType.Chain, 2, 500), paths), Is.False);
        Assert.That(SkillHitRules.ReachTooFar(new Vector3(0, 2000, 0), Vector3.Zero, Arrow(BounceType.Chain, 2, 500), paths), Is.True);
        // A range bounce starts its links where the one before hit, too.
        Assert.That(SkillHitRules.ReachTooFar(new Vector3(0, 1400, 0), Vector3.Zero, Arrow(BounceType.Range, 1, 500), paths), Is.False);
        // A non-targeted arrow flies its nonTargetMoveDistance.
        Assert.That(SkillHitRules.ReachTooFar(new Vector3(0, 1400, 0), Vector3.Zero, Arrow(), [Path(500, 800) with { NonTargetMoveDistance = 1300 }]), Is.False);
    }

    [Test]
    public void ArrowMovesAtItsPathsSpeedBetweenPackets() {
        var budget = new SkillHitBudget();
        Assert.That(budget.TakePoint(0, 0, held: false, count: 1, tick: 0), Is.True);
        budget.Declare(1, 0, 0);
        Assert.That(budget.TakeStep(2, Vector3.Zero, 0, 500), Is.False, "not declared");
        Assert.That(budget.TakeStep(1, new Vector3(0, 5000, 0), 0, 500), Is.True, "the first point");
        // 100 ms later: 500 * 0.6 s + 150 = 450 at most.
        Assert.That(budget.TakeStep(1, new Vector3(0, 5400, 0), 100, 500), Is.True);
        Assert.That(budget.TakeStep(1, new Vector3(0, 6000, 0), 200, 500), Is.False);
        // The point is kept: from 6000 on.
        Assert.That(budget.TakeStep(1, new Vector3(0, 6100, 0), 300, 500), Is.True);
    }
}
