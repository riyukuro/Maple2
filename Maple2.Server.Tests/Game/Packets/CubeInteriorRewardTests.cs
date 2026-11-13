using Maple2.Model.Error;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

public class CubeInteriorRewardTests {
    // Client: CSceneStateGame::OnResponseCube cases 0x28 and 0x29 read one byte, the error.
    [Test]
    public void ErrorAnswersAreModeAndOneByte() {
        ByteWriter checkIn = CubePacket.InteriorCheckInError(UgcMapError.s_ugcmap_cant_take_interior_gift_more);
        var reader = new ByteReader(checkIn.Buffer, 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.ResponseCube));
        Assert.That(reader.ReadByte(), Is.EqualTo(0x28));
        Assert.That(reader.ReadByte(), Is.EqualTo(84));
        Assert.That(checkIn.Length, Is.EqualTo(4));

        ByteWriter grade = CubePacket.InteriorGradeRewardError(UgcMapError.s_ugcmap_already_taken_interior_grade_gift);
        reader = new ByteReader(grade.Buffer, 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.ResponseCube));
        Assert.That(reader.ReadByte(), Is.EqualTo(0x29));
        Assert.That(reader.ReadByte(), Is.EqualTo(86));
        Assert.That(grade.Length, Is.EqualTo(4));
    }

    [Test]
    public void CheckInError() {
        Assert.That(InteriorRewardRules.CheckInError(true, true), Is.EqualTo(UgcMapError.s_ugcmap_cant_take_interior_gift_more));
        Assert.That(InteriorRewardRules.CheckInError(false, false), Is.EqualTo(UgcMapError.s_ugcmap_take_interior_gift_fail_from_db));
        Assert.That(InteriorRewardRules.CheckInError(false, true), Is.EqualTo(UgcMapError.s_ugcmap_ok));
    }

    // The row with the highest housingPoint at or below the score; the 0 row is never taken (GetDropBox).
    [TestCase(0, null)]
    [TestCase(99, null)]
    [TestCase(100, 100)]
    [TestCase(299, 100)]
    [TestCase(300, 300)]
    [TestCase(1099, 700)]
    [TestCase(5000, 1100)]
    public void RewardPoint(long score, int? expected) {
        int[] points = [0, 100, 300, 500, 700, 1100];
        Assert.That(InteriorRewardRules.RewardPoint(points, score), Is.EqualTo(expected));
        Assert.That(InteriorRewardRules.RewardPoint(new[] { 1100, 700, 500, 300, 100, 0 }, score), Is.EqualTo(expected));
    }

    [Test]
    public void GradeError() {
        int[] claimed = [1, 2];
        Assert.That(InteriorRewardRules.GradeError(claimed, 3, 2, true), Is.EqualTo(UgcMapError.s_ugcmap_already_taken_interior_grade_gift));
        Assert.That(InteriorRewardRules.GradeError(claimed, 3, 3, true), Is.EqualTo(UgcMapError.s_ugcmap_ok));
        Assert.That(InteriorRewardRules.GradeError(claimed, 3, 3, false), Is.EqualTo(UgcMapError.s_ugcmap_take_interior_gift_fail_from_db));
        // A grade the home has not reached is never offered by the client: no reward and no answer.
        Assert.That(InteriorRewardRules.GradeError(claimed, 3, 4, true), Is.Null);
        Assert.That(InteriorRewardRules.GradeError(claimed, 3, 0, true), Is.Null);
    }
}
