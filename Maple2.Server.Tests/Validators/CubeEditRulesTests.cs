using Maple2.Model.Error;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Validators;

public class CubeEditRulesTests {
    [TestCase(false, null, 50100189, UgcMapError.s_ugcmap_ok)]
    [TestCase(true, null, 50100189, UgcMapError.s_ugcmap_ok)]
    [TestCase(false, 50100189, 50100189, UgcMapError.s_ugcmap_create_on_non_empty_area)]
    [TestCase(false, 50200381, 50100189, UgcMapError.s_ugcmap_create_on_non_empty_area)]
    [TestCase(true, 50100189, 50100189, UgcMapError.s_ugcmap_cant_replace_same_cube)]
    [TestCase(true, 50200381, 50100189, UgcMapError.s_ugcmap_ok)]
    public void TakenCell(bool isReplace, int? existing, int item, UgcMapError expected) {
        Assert.That(CubeEditRules.Taken(isReplace, existing, item), Is.EqualTo(expected));
    }

    // The refusal keeps the edit's command and carries the object id twice, 10 bytes.
    [TestCase(CubePacket.Edit.Place, 10, UgcMapError.s_ugcmap_create_on_non_empty_area, 2)]
    [TestCase(CubePacket.Edit.Remove, 12, UgcMapError.s_ugcmap_no_cube_to_remove, 20)]
    [TestCase(CubePacket.Edit.Replace, 15, UgcMapError.s_ugcmap_cant_replace_same_cube, 53)]
    public void EditErrorBytes(CubePacket.Edit edit, int command, UgcMapError error, int code) {
        var reader = new ByteReader(CubePacket.EditError(edit, error, 5265895).ToArray(), 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.ResponseCube));
        Assert.That(reader.ReadByte(), Is.EqualTo(command));
        Assert.That(reader.ReadByte(), Is.EqualTo(code));
        Assert.That(reader.ReadInt(), Is.EqualTo(5265895));
        Assert.That(reader.ReadInt(), Is.EqualTo(5265895));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    // A refused lift: the lift command, the error, the object id, 6 bytes.
    [Test]
    public void LiftupErrorBytes() {
        var reader = new ByteReader(CubePacket.LiftupError(UgcMapError.s_ugcmap_no_cube_to_lift, 3596716).ToArray(), 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.ResponseCube));
        Assert.That(reader.ReadByte(), Is.EqualTo(17));
        Assert.That(reader.ReadByte(), Is.EqualTo(37));
        Assert.That(reader.ReadInt(), Is.EqualTo(3596716));
        Assert.That(reader.Available, Is.EqualTo(0));
    }
}
