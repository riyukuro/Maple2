using System.Numerics;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

public class AiStandbyFaceTests {
    // A standby node with faceTarget="1" turns the npc to its target; absent (0) and other values (3) do not.
    [TestCase(1, true)]
    [TestCase(0, false)]
    [TestCase(3, false)]
    public void OnlyFaceTargetOneTurns(int faceTarget, bool expected) {
        Assert.That(AiStandbyFace.FacesTarget(faceTarget), Is.EqualTo(expected));
    }

    [Test]
    public void DirectionPointsAtTheTarget() {
        Vector3? direction = AiStandbyFace.Direction(new Vector3(100, 200, 50), new Vector3(130, 240, 50));
        Assert.That(direction, Is.Not.Null);
        Assert.That(direction!.Value.X, Is.EqualTo(0.6f).Within(1e-5f));
        Assert.That(direction.Value.Y, Is.EqualTo(0.8f).Within(1e-5f));
        Assert.That(direction.Value.Z, Is.EqualTo(0f).Within(1e-5f));
    }

    [Test]
    public void SameSpotGivesNoDirection() {
        Assert.That(AiStandbyFace.Direction(new Vector3(7, 8, 9), new Vector3(7, 8, 9)), Is.Null);
    }
}
