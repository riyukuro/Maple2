using System;
using System.Numerics;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

public class AutoTargetFaceTests {
    private static readonly Vector3 Front = new(1, 0, 0);

    private static Vector3 At(float degrees, float distance, float height = 0) {
        float radians = degrees * MathF.PI / 180;
        return new Vector3(MathF.Cos(radians) * distance, MathF.Sin(radians) * distance, height);
    }

    // MaxDegree is the whole cone width in degrees: 90 lets a target 40 degrees off through and stops one 50 off.
    [TestCase(40f, true)]
    [TestCase(50f, false)]
    [TestCase(-40f, true)]
    public void ConeIsTheWholeWidthInDegrees(float degrees, bool expected) {
        Assert.That(AutoTargetFace.Passes(At(degrees, 200), Front, 90, 0, 0), Is.EqualTo(expected));
    }

    // 210 (the client's default) reaches 105 degrees each side; 360 takes a target behind.
    [TestCase(210f, 100f, true)]
    [TestCase(210f, 110f, false)]
    [TestCase(360f, 180f, true)]
    public void WideCones(float maxDegree, float degrees, bool expected) {
        Assert.That(AutoTargetFace.Passes(At(degrees, 200), Front, maxDegree, 0, 0), Is.EqualTo(expected));
    }

    // MaxDistance is a plain ground radius, not a squared one; 0 sets no limit here.
    [TestCase(300f, 290f, true)]
    [TestCase(300f, 310f, false)]
    [TestCase(0f, 5000f, true)]
    public void RadiusIsPlainDistance(float maxDistance, float distance, bool expected) {
        Assert.That(AutoTargetFace.Passes(At(0, distance), Front, 210, maxDistance, 0), Is.EqualTo(expected));
    }

    // A height of 0 is the client's 150; the cylinder is centred on the caster. All checks must pass.
    [TestCase(0f, 70f, true)]
    [TestCase(0f, 80f, false)]
    [TestCase(400f, -190f, true)]
    [TestCase(400f, 210f, false)]
    public void HeightIsTheCylinderHeight(float maxHeight, float height, bool expected) {
        Assert.That(AutoTargetFace.Passes(At(0, 100, height), Front, 210, 300, maxHeight), Is.EqualTo(expected));
    }

    [Test]
    public void OutsideTheConeFailsEvenInRange() {
        Assert.That(AutoTargetFace.Passes(At(170, 50), Front, 90, 300, 0), Is.False);
    }
}
