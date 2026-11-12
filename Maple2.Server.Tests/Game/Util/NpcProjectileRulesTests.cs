using System.Collections.Generic;
using System.Numerics;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Util;
using Maple2.Tools.Collision;

namespace Maple2.Server.Tests.Game.Util;

public class NpcProjectileRulesTests {
    private static MagicPath Path(float distance = 4000, float velocity = 500, bool rotate = true, float fireZ = 80,
        float delay = 1, Vector3 control0 = default, int interval = 0, int maxTargets = 0, float rotateZ = 0) {
        return new MagicPath(Align: false, AlignHeight: 0, Rotate: rotate, IgnoreAdjust: false, ExplosionByDestroy: true,
            CatmullRom: false, IgnorePhysXTestInitPosition: false, IgnoreCancelAtSpawnTime: false,
            Direction: new Vector3(0, 1, 0), FireOffset: new Vector3(0, 0, fireZ), FireFixed: default, ControlValue0: control0,
            ControlValue1: default, ControlEndOffsetValue: default, TraceTargetOffset: false,
            TraceTargetDuration: 0, Velocity: velocity, Distance: distance, RotateZDegree: rotateZ, LifeTime: 0, DelayTime: delay,
            SpawnTime: 0, DestroyTime: 0.1f, ControlRate: 0, LookAtType: 0, PiercingAttackInterval: interval,
            PiercingAttackMaxTargetCount: maxTargets, NonTargetMoveDistance: 0, MoveEndHoldDuration: 0);
    }

    // The wave's arrow (49200001): 280 wide, 50 ahead, 125 up, 25 ahead of the last position too, 50 down; pierces 50.
    private static SkillMetadataArrow Wave(BounceType type = BounceType.Pierce, int count = 50) {
        return new SkillMetadataArrow(Overlap: true, Explosion: false, RayPhysXTest: false, NonTarget: SkillTargetType.Owner,
            BounceType: type, BounceCount: count, BounceRadius: 0, BounceOverlap: false,
            Collision: new Vector3(280, 50, 125), CollisionAdd: new Vector3(0, -25, 50), RayType: 0);
    }

    // A player standing at the point: a circle of 10, 100 high (FieldPlayer.Shape).
    private static Prism Player(float x, float y, float z = 0) => new(new Circle(new Vector2(x, y), 10), z, 100);

    [Test]
    public void AStraightPathWithACollisionBoxIsSwept() {
        Assert.That(NpcProjectileRules.Sweeps(Wave(), Path()), Is.True);
        // A curve (the cannon's control points) is not.
        Assert.That(NpcProjectileRules.Sweeps(Wave(), Path(control0: new Vector3(0, 0.5f, 600))), Is.False);
        // A path that does not fly, or an arrow without a box, is not.
        Assert.That(NpcProjectileRules.Sweeps(Wave(), Path(velocity: 0)), Is.False);
        SkillMetadataArrow flat = Wave() with { Collision = default, CollisionAdd = default };
        Assert.That(NpcProjectileRules.Sweeps(flat, Path()), Is.False);
    }

    [Test]
    public void TheShotStartsAtTheFireOffsetAndEndsAtTheDistanceAlongTheCastDirection() {
        Vector3 forward = NpcProjectileRules.Forward(new Vector3(1, 0, 0), Path());
        Assert.That(forward.X, Is.EqualTo(1).Within(1e-5));
        Vector3 start = NpcProjectileRules.Start(new Vector3(100, 200, 300), forward, Path());
        Assert.That(start, Is.EqualTo(new Vector3(100, 200, 380)));
        Vector3 end = NpcProjectileRules.End(start, forward, Path());
        Assert.That(end.X, Is.EqualTo(4100).Within(1e-2));
        Assert.That(end.Y, Is.EqualTo(200).Within(1e-2));
        Assert.That(end.Z, Is.EqualTo(380).Within(1e-2));
        // Without rotate the path is in world space.
        Assert.That(NpcProjectileRules.Forward(new Vector3(1, 0, 0), Path(rotate: false)), Is.EqualTo(Vector3.UnitY));
    }

    [Test]
    public void ASideOffsetTurnsWithTheForward() {
        Vector3 local = NpcProjectileRules.Local(Vector3.UnitY, new Vector3(10, 0, 0));
        Assert.That(local.X, Is.EqualTo(-10).Within(1e-5));
        Vector3 turned = NpcProjectileRules.Local(Vector3.UnitX, new Vector3(10, 20, 5));
        Assert.That(turned, Is.EqualTo(new Vector3(20, 10, 5)));
    }

    [Test]
    public void TheShotMovesAtItsVelocityAndStopsAtTheGoal() {
        (Vector3 next, bool arrived) = NpcProjectileRules.Move(default, new Vector3(0, 1000, 0), 500, 0.1f);
        Assert.That(next.Y, Is.EqualTo(50).Within(1e-3));
        Assert.That(arrived, Is.False);
        (next, arrived) = NpcProjectileRules.Move(new Vector3(0, 980, 0), new Vector3(0, 1000, 0), 500, 0.1f);
        Assert.That(next, Is.EqualTo(new Vector3(0, 1000, 0)));
        Assert.That(arrived, Is.True);
    }

    [Test]
    public void TheBoxCoversAPlayerInTheStepAndJustAheadOfIt() {
        // A step of 50 along +Y at a height of 80 over the ground.
        Prism box = NpcProjectileRules.SweepBox(new Vector3(0, 0, 80), new Vector3(0, 50, 80), Vector3.UnitY, Wave());
        Assert.That(box.Intersects(Player(0, 25)), Is.True);
        // collision.Y = 50 past the new position (plus the player's radius).
        Assert.That(box.Intersects(Player(0, 105)), Is.True);
        Assert.That(box.Intersects(Player(0, 115)), Is.False);
        // collisionAdd.Y = -25: the box starts 25 ahead of the last position.
        Assert.That(box.Intersects(Player(0, 20)), Is.True);
        Assert.That(box.Intersects(Player(0, 10)), Is.False);
        // 280 wide: 140 to each side.
        Assert.That(box.Intersects(Player(145, 40)), Is.True);
        Assert.That(box.Intersects(Player(155, 40)), Is.False);
        // From 50 below the shot to 125 above it.
        Assert.That(box.Intersects(Player(0, 40, -60)), Is.True);
        Assert.That(box.Intersects(Player(0, 40, 210)), Is.False);
    }

    [Test]
    public void AShotAtChestHeightHitsThePlayersBoxButNotTheServersShape() {
        // A shot fired 130 up with no box below it (40104001's path 5055 is 130 to 260 over its own ground).
        SkillMetadataArrow arrow = Wave() with { CollisionAdd = new Vector3(0, -25, 20) };
        Prism box = NpcProjectileRules.SweepBox(new Vector3(0, 0, 130), new Vector3(0, 50, 130), Vector3.UnitY, arrow);
        Assert.That(box.Intersects(Player(0, 40)), Is.False);
        Assert.That(box.Intersects(NpcProjectileRules.PlayerBox(new Vector3(0, 40, 0))), Is.True);
        // The query box finds the player's server shape for every box that can touch its hit box.
        Assert.That(NpcProjectileRules.QueryBox(box).Intersects(Player(0, 40)), Is.True);
    }

    [Test]
    public void ADescendingStepCoversBothItsEnds() {
        Prism box = NpcProjectileRules.SweepBox(new Vector3(0, 0, 600), new Vector3(0, 50, 300), Vector3.UnitY, Wave());
        Assert.That(box.Height.Min, Is.EqualTo(250).Within(1e-3));
        Assert.That(box.Height.Max, Is.EqualTo(725).Within(1e-3));
    }

    [Test]
    public void AFollowingShotAimsAtTheMiddleOfItsTarget() {
        Assert.That(NpcProjectileRules.AimPoint(new Vector3(1, 2, 3), NpcProjectileRules.PlayerHeight), Is.EqualTo(new Vector3(1, 2, 78)));
    }

    [Test]
    public void TheBoxTurnsWithTheTravel() {
        Prism box = NpcProjectileRules.SweepBox(new Vector3(0, 0, 0), new Vector3(100, 0, 0), Vector3.UnitY, Wave());
        Assert.That(box.Intersects(Player(150, 0)), Is.True);
        Assert.That(box.Intersects(Player(0, 150)), Is.False);
        Assert.That(box.Intersects(Player(50, 130)), Is.True);
    }

    [Test]
    public void APiercingShotHitsEachActorOnceUpToItsCount() {
        var hits = new NpcProjectileRules.Hits(Wave(count: 2), Path());
        Assert.That(hits.Take([1], arrived: false), Is.EqualTo(new List<int> { 1 }));
        Assert.That(hits.Excluded(100), Does.Contain(1));
        Assert.That(hits.Take([2, 3], arrived: false), Is.EqualTo(new List<int> { 2 }));
        Assert.That(hits.Done, Is.True);
        Assert.That(hits.Take([3], arrived: true), Is.Empty);
    }

    [Test]
    public void APiercingIntervalLetsTheShotHitTheSameActorAgain() {
        var hits = new NpcProjectileRules.Hits(Wave(), Path(interval: 500));
        Assert.That(hits.Take([1], arrived: false), Has.Count.EqualTo(1));
        Assert.That(hits.Excluded(400), Does.Contain(1));
        Assert.That(hits.Excluded(500), Is.Empty);
        // At most maxTargets between two intervals.
        var capped = new NpcProjectileRules.Hits(Wave(), Path(interval: 500, maxTargets: 2));
        Assert.That(capped.Take([1, 2, 3], arrived: false), Is.EqualTo(new List<int> { 1, 2 }));
        Assert.That(capped.Take([3], arrived: false), Is.Empty);
        capped.Excluded(500);
        Assert.That(capped.Take([3], arrived: false), Is.EqualTo(new List<int> { 3 }));
    }

    [Test]
    public void AShotThatDoesNotPierceStopsAtItsFirstHit() {
        var hits = new NpcProjectileRules.Hits(Wave(type: BounceType.None, count: 0), Path());
        Assert.That(hits.Take([4, 5], arrived: false), Is.EqualTo(new List<int> { 4 }));
        Assert.That(hits.Done, Is.True);
    }

    [Test]
    public void AChainShotHitsOnlyWhereItsFlightEnds() {
        var hits = new NpcProjectileRules.Hits(Wave(type: BounceType.Chain, count: 1), Path());
        Assert.That(hits.Take([4], arrived: false), Is.Empty);
        Assert.That(hits.Take([4], arrived: true), Is.EqualTo(new List<int> { 4 }));
    }

    [Test]
    public void TheShotWaitsItsDelayAndSpawnTime() {
        Assert.That(NpcProjectileRules.Delay(Path(delay: 1) with { SpawnTime = 0.5f }), Is.EqualTo(System.TimeSpan.FromSeconds(1.5)));
    }
}
