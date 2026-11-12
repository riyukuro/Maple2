using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Maple2.Model.Enum;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

// The AI jump node, as CNpc::ApplySyncBlock and the client's arc read it.
public class AiJumpTests {
    // AI_Bliche_FR_MoonSlash jumps between two arena points at speed 2200 with heightMultiplier 0.7.
    private static readonly Vector3 Left = new(2228, 830, 7951);
    private static readonly Vector3 Right = new(3037, 830, 7951);

    [Test]
    public void TypeTwoIsJumpB() {
        Assert.Multiple(() => {
            Assert.That(AiJump.Sequence(NodeJumpType.JumpA), Is.EqualTo(NpcControlPacket.ANI_JUMP_A));
            Assert.That(AiJump.Sequence(NodeJumpType.JumpB), Is.EqualTo(NpcControlPacket.ANI_JUMP_B));
            Assert.That(AiJump.Sequence(NodeJumpType.JumpA), Is.EqualTo(-2));
            Assert.That(AiJump.Sequence(NodeJumpType.JumpB), Is.EqualTo(-3));
        });
    }

    [Test]
    public void DurationIsDistanceOverSpeed() {
        Assert.Multiple(() => {
            Assert.That(AiJump.Duration(Left, Right, 2200), Is.EqualTo(809f / 2200f).Within(1e-6f));
            Assert.That(AiJump.Duration(Vector3.Zero, new Vector3(0, 0, 1500), 1500), Is.EqualTo(1f).Within(1e-6f));
        });
    }

    // A jump in place or a bad speed takes the client's relative-jump duration, never 0 (the arc divides by it).
    [TestCase(0)]
    [TestCase(-100)]
    public void BadSpeedTakesTheClientDuration(int speed) {
        Assert.That(AiJump.Duration(Left, Right, speed), Is.EqualTo(AiJump.DefaultDuration));
    }

    [Test]
    public void JumpInPlaceTakesTheClientDuration() {
        Assert.That(AiJump.Duration(Left, Left, 1500), Is.EqualTo(AiJump.DefaultDuration));
    }

    // 150/212.132 of the distance at scale 1: a 212.132 jump peaks 150 above the line.
    [Test]
    public void HeightIsTheClientArc() {
        Assert.Multiple(() => {
            Assert.That(AiJump.Height(Vector3.Zero, new Vector3(150, 150, 0), 1f), Is.EqualTo(150f).Within(0.01f));
            Assert.That(AiJump.Height(Left, Right, 0.7f), Is.EqualTo(150f / 212.132f * 809f * 0.7f).Within(0.01f));
        });
    }

    // The server walks the npc along the client's arc: the ends, then the top at the middle.
    [Test]
    public void PointFollowsTheClientArc() {
        float top = AiJump.Height(Left, Right, 0.7f);
        Assert.Multiple(() => {
            Assert.That(AiJump.Point(Left, Right, 0.7f, 0f), Is.EqualTo(Left));
            Assert.That(AiJump.Point(Left, Right, 0.7f, 1f), Is.EqualTo(Right));
            Vector3 middle = AiJump.Point(Left, Right, 0.7f, 0.5f);
            Assert.That(middle.X, Is.EqualTo((Left.X + Right.X) / 2).Within(1e-3f));
            Assert.That(middle.Z, Is.EqualTo(Left.Z + top).Within(1e-3f));
            Assert.That(AiJump.Point(Left, Right, 0.7f, 0.25f).Z, Is.EqualTo(Left.Z + 0.75f * top).Within(1e-3f));
        });
    }

    [Test]
    public void PointClampsPastTheEnds() {
        Assert.Multiple(() => {
            Assert.That(AiJump.Point(Left, Right, 0.7f, -1f), Is.EqualTo(Left));
            Assert.That(AiJump.Point(Left, Right, 0.7f, 2f), Is.EqualTo(Right));
        });
    }
}
