using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Validators;

public class MeretMarketPacketTests {
    // MeretMarket 0x0039 mode 20 sends i64 sale id; the answer the client reads is
    // u8 mode 20, i64 sale id, i32 result, i64 amount.
    [Test]
    public void CollectProfitAnswer() {
        byte[] bytes = MeretMarketPacket.CollectProfit(0x1122334455667788, 450).ToArray();
        Assert.That(bytes.Length, Is.EqualTo(2 + 1 + 8 + 4 + 8));
        var packet = new ByteReader(bytes, 2);
        Assert.That(packet.ReadByte(), Is.EqualTo(20));
        Assert.That(packet.ReadLong(), Is.EqualTo(0x1122334455667788));
        Assert.That(packet.ReadInt(), Is.EqualTo(1018));
        Assert.That(packet.ReadLong(), Is.EqualTo(450));
        Assert.That(packet.Available, Is.EqualTo(0));
    }
}
