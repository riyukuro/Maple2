using System;
using System.Numerics;
using Maple2.Model.Game;
using Maple2.Model.Metadata;

namespace Maple2.Server.Tests.Game;

public class TriggerBoxTests {
    private static TriggerBox Box(float rotZ) =>
        new(new Ms2TriggerBox(new Vector3(1000, 2000, 300), new Vector3(900, 150, 200), 9005, true, new Vector3(0, 0, rotZ)));

    private static Vector3 At(float distance, float angleDeg) {
        float radians = angleDeg * MathF.PI / 180;
        return new Vector3(1000 + distance * MathF.Cos(radians), 2000 + distance * MathF.Sin(radians), 300);
    }

    [Test]
    public void RotatedBoxFollowsItsLongAxis() {
        TriggerBox box = Box(315);
        Assert.That(box.Contains(At(400, 315)), Is.True);
        Assert.That(box.Contains(At(400, 135)), Is.True);
        Assert.That(box.Contains(At(400, 0)), Is.False);
        Assert.That(box.Contains(At(400, 45)), Is.False);
    }

    [Test]
    public void UnturnedBoxKeepsAxisAlignedExtent() {
        TriggerBox box = Box(0);
        Assert.That(box.Contains(At(400, 0)), Is.True);
        Assert.That(box.Contains(At(400, 315)), Is.False);
    }

    [Test]
    public void HalfTurnMatchesUnturned() {
        TriggerBox turned = Box(180);
        TriggerBox unturned = Box(0);
        foreach (float angle in new[] { 0f, 30f, 90f, 315f }) {
            foreach (float distance in new[] { 50f, 80f, 400f, 470f }) {
                Assert.That(turned.Contains(At(distance, angle)), Is.EqualTo(unturned.Contains(At(distance, angle))), $"{distance} at {angle}");
            }
        }
    }

    [Test]
    public void RotatedBoxKeepsHeight() {
        TriggerBox box = Box(225);
        Vector3 inside = At(300, 225);
        Assert.That(box.Contains(inside), Is.True);
        Assert.That(box.Contains(inside with { Z = 300 + 200 }), Is.False);
        Assert.That(box.Contains(inside with { Z = 300 - 120 }), Is.False);
    }

    [Test]
    public void MissingRotationLoadsAsUnturned() {
        var metadata = new Ms2TriggerBox(Vector3.Zero, new Vector3(100, 100, 100), 1, true);
        Assert.That(metadata.Rotation, Is.EqualTo(Vector3.Zero));
    }
}
