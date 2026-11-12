using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Tests.Game.Packets;

// BindItem 0x103 as CSceneStateGame::OnBindItem reads it: the uid, then the item stream.
public class ItemBindPacketTests {
    private const int ItemId = 39990002;

    private static Item ItemOf(TransferType transferType) {
        var property = new ItemMetadataProperty(false, 0, 100, 18, 0, string.Empty, string.Empty, ItemTag.None, 0, 0, 0, 0, 0, 0, 0, 0, 0, [], false, 0, false, [], [], [], 0, 0);
        var limit = new ItemMetadataLimit(Gender.All, 0, 0, 4, true, true, true, true, true, false, false, (int) transferType, [], []);
        var metadata = new ItemMetadata(ItemId, ItemId.ToString(), [], "", [], new ItemMetadataLife(0, 0), property, new ItemMetadataCustomize(0, 0), limit, null, null, [], null, null, null, null);
        return new Item(metadata, 1, 1) { Uid = 0x1122334455667788 };
    }

    [Test]
    public void BindBytes() {
        Item item = ItemOf(TransferType.BindPet);
        var stream = new ByteWriter();
        stream.WriteClass<Item>(item);

        byte[] packet = ItemBindPacket.Bind(item).ToArray();
        var reader = new ByteReader(packet, 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.BindItem));
        Assert.That((ushort) SendOp.BindItem, Is.EqualTo(0x103));
        Assert.That(reader.ReadLong(), Is.EqualTo(0x1122334455667788));
        Assert.That(packet[10..], Is.EqualTo(stream.ToArray()));
    }
}
