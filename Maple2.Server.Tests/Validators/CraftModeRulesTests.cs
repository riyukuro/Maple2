using Maple2.Model.Game;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Tests.Validators;

public class CraftModeRulesTests {
    // Start with an empty palette: u8 1, u8 0 and nothing after it.
    [Test]
    public void StartWithoutCube() {
        var writer = new ByteWriter();
        writer.WriteByte(1);
        writer.WriteByte(0);
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(CraftModeRules.ReadCube(reader), Is.Null);
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    // Start with a cube: u8 1, u8 1, i32 item id, i64 uid, i64, u8 no look.
    [Test]
    public void StartWithCube() {
        var writer = new ByteWriter();
        writer.WriteByte(1);
        writer.WriteByte(1);
        writer.WriteInt(50200001);
        writer.WriteLong(0x1122334455667788);
        writer.WriteLong(0);
        writer.WriteByte(0);
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        PlotCube? cube = CraftModeRules.ReadCube(reader);
        Assert.That(cube, Is.Not.Null);
        Assert.That(cube!.ItemId, Is.EqualTo(50200001));
        Assert.That(cube.Id, Is.EqualTo(0x1122334455667788));
        Assert.That(reader.Available, Is.EqualTo(0));
    }
}
