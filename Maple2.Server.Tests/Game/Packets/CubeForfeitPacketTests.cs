using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// Client: CSceneStateGame::OnResponseCube case 7 reads a short reason, a byte flag and two ints.
public class CubeForfeitPacketTests {
    [Test]
    public void ConfirmForfeitPlotIsShortByteIntInt() {
        var cost = new UgcMapGroup.Cost(0, 0, 0);
        var plot = new PlotInfo(new UgcMapGroup(7, 0, 0, cost, cost, new UgcMapGroup.Limits(0, 0, 0, 0, 0, 0))) {
            MapId = 2000062,
            Number = 7,
        };
        ByteWriter writer = CubePacket.ConfirmForfeitPlot(plot, 0x75, true);
        var reader = new ByteReader(writer.Buffer, 0);
        Assert.Multiple(() => {
            Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.ResponseCube));
            Assert.That(reader.ReadByte(), Is.EqualTo(7));
            Assert.That(reader.ReadShort(), Is.EqualTo(0x75));
            Assert.That(reader.ReadByte(), Is.EqualTo(1));
            Assert.That(reader.ReadInt(), Is.EqualTo(2000062));
            Assert.That(reader.ReadInt(), Is.EqualTo(7));
            Assert.That(writer.Length, Is.EqualTo(2 + 1 + 2 + 1 + 4 + 4));
        });
    }
}
