using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Validators;

public class ItemMergePacketTests {
    // ItemMerge 0x003F mode 0 has no payload; the answer the client reads is u8 mode 0, u8 0.
    [Test]
    public void InfoAnswerIsModeZeroAndNothingPending() {
        byte[] bytes = ItemMergePacket.NoPendingChoice().ToArray();
        Assert.That(bytes.Length, Is.EqualTo(4));
        var packet = new ByteReader(bytes, 2);
        Assert.That(packet.ReadByte(), Is.EqualTo(0));
        Assert.That(packet.ReadByte(), Is.EqualTo(0));
        Assert.That(packet.Available, Is.EqualTo(0));
    }
}
