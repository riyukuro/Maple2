using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// FieldPickupItem as CSceneStateGame::OnFieldPickupItem reads a refusal: picked 0, the drop, mode 1.
public class ItemPickupRefuseTests {
    [Test]
    public void RefuseBytes() {
        ByteWriter packet = ItemPickupPacket.Refuse(0x2A3B4C);
        var reader = new ByteReader(packet.ToArray(), 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.FieldPickupItem));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(0x2A3B4C));
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.Available, Is.EqualTo(0));
    }
}
