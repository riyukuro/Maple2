using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

public class MeretMarketAdProductsTests {
    // Client: CMeratMarketMan::OnAdProducts (client/ms2/meratmarketman.cpp:2129) reads a byte count and then the
    // entries. KMS2 answers MeretMarket 103 with the mode and a zero count.
    [Test]
    public void EmptyList() {
        ByteWriter writer = MeretMarketPacket.AdProducts();
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.MeretMarket));
        Assert.That(reader.ReadByte(), Is.EqualTo(103));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(writer.Length, Is.EqualTo(2 + 1 + 1));
    }
}
