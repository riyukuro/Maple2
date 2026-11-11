using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

public class ItemDismantleListTests {
    // Client: CItemBreakManager::OnItemDismantlePacket (itembreakmanager.cpp:389): mode 4 reads one int result.
    [Test]
    public void ErrorIsOneInt() {
        ByteWriter writer = ItemDismantlePacket.Error(5);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.ItemDismantle));
        Assert.That(reader.ReadByte(), Is.EqualTo(4));
        Assert.That(reader.ReadInt(), Is.EqualTo(5));
        Assert.That(writer.Length, Is.EqualTo(2 + 1 + 4));
    }

    // Client: mode 6 (itembreakmanager.cpp:397): int count, count x (short slot, long item uid, int amount).
    [Test]
    public void StageListEntries() {
        ByteWriter writer = ItemDismantlePacket.StageList([(0, 1001L, 1), (3, 1002L, 7)]);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.ItemDismantle));
        Assert.That(reader.ReadByte(), Is.EqualTo(6));
        Assert.That(reader.ReadInt(), Is.EqualTo(2));
        Assert.That(reader.ReadShort(), Is.EqualTo(0));
        Assert.That(reader.ReadLong(), Is.EqualTo(1001L));
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.ReadShort(), Is.EqualTo(3));
        Assert.That(reader.ReadLong(), Is.EqualTo(1002L));
        Assert.That(reader.ReadInt(), Is.EqualTo(7));
        Assert.That(writer.Length, Is.EqualTo(2 + 1 + 4 + 2 * 14));
    }
}
