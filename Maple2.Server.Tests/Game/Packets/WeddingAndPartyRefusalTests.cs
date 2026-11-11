using Maple2.Model.Error;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

public class WeddingAndPartyRefusalTests {
    // Wedding 9 (divorce answer): WeddingManager::OnResult reads one byte; 0x2d shows
    // s_wedding_result_invaild_state_divorce.
    [Test]
    public void DivorceAnswerRefusalIs45() {
        ByteWriter writer = WeddingPacket.Error(WeddingError.s_wedding_result_invaild_state_divorce);
        var reader = new ByteReader(writer.Buffer, 0);
        Assert.Multiple(() => {
            Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Wedding));
            Assert.That(reader.ReadByte(), Is.EqualTo(2));
            Assert.That(reader.ReadByte(), Is.EqualTo(45));
            Assert.That(writer.Length, Is.EqualTo(2 + 1 + 1));
        });
    }
}
