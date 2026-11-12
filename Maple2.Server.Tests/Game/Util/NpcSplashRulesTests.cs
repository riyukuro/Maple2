using System;
using System.Linq;
using System.Numerics;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

public class NpcSplashRulesTests {
    private static MagicPath Path(float distance, float velocity, bool rotate = true, float endZ = 0) {
        return new MagicPath(Align: false, AlignHeight: 0, Rotate: rotate, IgnoreAdjust: false, ExplosionByDestroy: true,
            CatmullRom: false, IgnorePhysXTestInitPosition: false, IgnoreCancelAtSpawnTime: false,
            Direction: new Vector3(0, 1, 0), FireOffset: default, FireFixed: default, ControlValue0: default,
            ControlValue1: default, ControlEndOffsetValue: new Vector3(0, 0, endZ), TraceTargetOffset: false,
            TraceTargetDuration: 0.5f, Velocity: velocity, Distance: distance, RotateZDegree: 0, LifeTime: 0, DelayTime: 0,
            SpawnTime: 0, DestroyTime: 0.1f, ControlRate: 0, LookAtType: 0, PiercingAttackInterval: 0,
            PiercingAttackMaxTargetCount: 0, NonTargetMoveDistance: 0, MoveEndHoldDuration: 0);
    }

    [Test]
    public void AShotWithANoTargetSplashLandsItWhetherOrNotTheRangeHoldsATarget() {
        Assert.That(NpcSplashRules.Where(noTargetSplash: true, flies: true, targetsInRange: 0), Is.EqualTo(NpcSplashRules.Place.Landing));
        Assert.That(NpcSplashRules.Where(noTargetSplash: true, flies: true, targetsInRange: 2), Is.EqualTo(NpcSplashRules.Place.Landing));
    }

    [Test]
    public void ANoTargetSplashWithoutAShotPlacesNothingWhenNothingIsHit() {
        Assert.That(NpcSplashRules.Where(noTargetSplash: true, flies: false, targetsInRange: 0), Is.EqualTo(NpcSplashRules.Place.None));
        Assert.That(NpcSplashRules.Where(noTargetSplash: true, flies: false, targetsInRange: 1), Is.EqualTo(NpcSplashRules.Place.Targets));
    }

    [Test]
    public void ASplashThatNeedsATargetStaysWithTheTargets() {
        Assert.That(NpcSplashRules.Where(noTargetSplash: false, flies: true, targetsInRange: 0), Is.EqualTo(NpcSplashRules.Place.None));
        Assert.That(NpcSplashRules.Where(noTargetSplash: false, flies: true, targetsInRange: 1), Is.EqualTo(NpcSplashRules.Place.Targets));
        Assert.That(NpcSplashRules.Where(noTargetSplash: false, flies: false, targetsInRange: 0), Is.EqualTo(NpcSplashRules.Place.None));
    }

    [Test]
    public void AShotReachesItsDistancePlusOneBlockOnTheGround() {
        MagicPath path = Path(800, 1200);
        Assert.That(NpcSplashRules.Reaches(new Vector3(0, 950, -2000), default, path), Is.True);
        Assert.That(NpcSplashRules.Reaches(new Vector3(0, 951, 0), default, path), Is.False);
        // A target 3,000 away is no aim for a path of 800.
        Assert.That(NpcSplashRules.Reaches(new Vector3(3000, 0, 0), default, path), Is.False);
    }

    [Test]
    public void ALandedShotLeavesTheTargetNeedingSplashesWithTheTargets() {
        // Odd numbers stand for the splashes that need no target.
        int[] splashes = [1, 2, 3, 4];
        Assert.That(NpcSplashRules.AtTargets(splashes, splash => splash % 2 == 1, landed: true), Is.EqualTo(new[] { 2, 4 }));
        Assert.That(NpcSplashRules.AtTargets(splashes, splash => splash % 2 == 1, landed: false), Is.EqualTo(splashes));
    }

    [Test]
    public void OnlyAPathWithDistanceAndVelocityFlies() {
        Assert.That(NpcSplashRules.Flies(Path(2500, 1200)), Is.True);
        Assert.That(NpcSplashRules.Flies(Path(0, 1200)), Is.False);
        Assert.That(NpcSplashRules.Flies(Path(2500, 0)), Is.False);
    }

    [Test]
    public void AnAimedShotLandsOnTheTargetsCell() {
        // A monster 2,450 away and 449 below the caster: the region is on its cell.
        Assert.That(NpcSplashRules.LandPoint(new Vector3(-140, -760, 1650)), Is.EqualTo(new Vector3(-150, -750, 1650)));
        Assert.That(NpcSplashRules.LandPoint(new Vector3(20, -440, 1655)), Is.EqualTo(new Vector3(0, -450, 1650)));
    }

    [Test]
    public void AShotWithoutAnAimEndsAtThePathsDistanceAndEndHeight() {
        Vector3 end = NpcSplashRules.EndPoint(new Vector3(2300, -825, 2100), default, Path(2500, 1200, rotate: false, endZ: -500));
        Assert.That(end, Is.EqualTo(new Vector3(2250, 1650, 1500)));
        Vector3 turned = NpcSplashRules.EndPoint(new Vector3(0, 0, 0), new Vector3(0, 0, 90), Path(1500, 1200));
        Assert.That(new Vector2(turned.X, turned.Y).Length(), Is.EqualTo(1500).Within(1));
        Assert.That(MathF.Abs(turned.Y), Is.LessThan(1));
    }

    [Test]
    public void TheFlightTakesTheGroundDistanceAtThePathsVelocity() {
        MagicPath path = Path(2500, 1200);
        Assert.That(NpcSplashRules.Flight(new Vector3(2300, -825, 2100), new Vector3(-150, -750, 1650), path).TotalSeconds, Is.EqualTo(2.043).Within(0.001));
        Assert.That(NpcSplashRules.Flight(default, new Vector3(0, 0, -900), path), Is.EqualTo(TimeSpan.Zero));
        Assert.That(NpcSplashRules.Flight(default, new Vector3(100, 0, 0), Path(2500, 0)), Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void AimsAreTheNearestActorsThePathReaches() {
        Vector3[] pool = [new(3000, 0, 0), new(2600, 0, -450), new(900, 0, 0), new(0, 2400, 0)];
        Vector3[] aims = NpcSplashRules.Aims(pool, position => position, default, Path(2500, 1200)).ToArray();
        Assert.That(aims, Is.EqualTo(new Vector3[] { new(900, 0, 0), new(0, 2400, 0), new(2600, 0, -450) }));
    }
}
