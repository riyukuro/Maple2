using System.Numerics;
using Maple2.Tools.VectorMath;

namespace Maple2.Server.Tests.Tools;

// Transform.LookTo with a direction that has no facing on the ground plane.
public class TransformTests {
    private static Transform Facing(Vector3 direction) {
        var transform = new Transform();
        transform.LookTo(direction);
        return transform;
    }

    private static void AssertFacing(Transform transform, Vector3 expected) {
        Vector3 front = transform.FrontAxis;
        Assert.That(front.X, Is.EqualTo(expected.X).Within(1e-4f));
        Assert.That(front.Y, Is.EqualTo(expected.Y).Within(1e-4f));
        Assert.That(front.Z, Is.EqualTo(expected.Z).Within(1e-4f));
        Assert.That(transform.Scale, Is.EqualTo(1).Within(1e-4f));
    }

    [Test]
    public void LookTo_FacesTheDirectionOnTheGroundPlane() {
        AssertFacing(Facing(new Vector3(3, 0, 4)), new Vector3(1, 0, 0));
        AssertFacing(Facing(new Vector3(0, -2, 0)), new Vector3(0, -1, 0));
    }

    // A target on the npc's own spot, straight above or below it, or a NaN offset: the facing is kept.
    [Test]
    public void LookTo_WithoutAFacing_KeepsTheFacing() {
        Transform transform = Facing(new Vector3(0, 1, 0));
        transform.LookTo(new Vector3(0, 0, 0));
        AssertFacing(transform, new Vector3(0, 1, 0));
        transform.LookTo(new Vector3(0, 0, 5));
        AssertFacing(transform, new Vector3(0, 1, 0));
        transform.LookTo(new Vector3(0, 0, -5), new Vector3(0, 0, 1), false);
        AssertFacing(transform, new Vector3(0, 1, 0));
        transform.LookTo(new Vector3(float.NaN, 0, 0));
        AssertFacing(transform, new Vector3(0, 1, 0));
        transform.LookTo(Vector3.Normalize(new Vector3(0, 0, 0)));
        AssertFacing(transform, new Vector3(0, 1, 0));
    }

    // Before, the NaN stayed: Scale is read back from the axes, so the next LookTo kept it too.
    [Test]
    public void LookTo_AfterADirectionWithoutAFacing_StillTurns() {
        Transform transform = Facing(new Vector3(1, 0, 0));
        transform.LookTo(new Vector3(0, 0, 1));
        transform.LookTo(new Vector3(-1, 0, 0));
        AssertFacing(transform, new Vector3(-1, 0, 0));
        Assert.That(float.IsFinite(transform.RotationAnglesDegrees.Z), Is.True);
    }
}
