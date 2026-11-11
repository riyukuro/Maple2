using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

public class CubeBuildModePacketTests {
    // Client: ResponseCube case 0 (scenestategame.cpp:9883) reads int player object id, int mode.
    [Test]
    public void BuildModeIsObjectIdThenMode() {
        ByteWriter writer = CubePacket.BuildMode(0x11223344, 1);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.ResponseCube));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(0x11223344));
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(writer.Length, Is.EqualTo(11));
    }
}
