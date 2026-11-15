using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;
using Maple2.Tools.Extensions;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game.Packets;

// ItemInventory 0x21's list modes against the client's readers. Mode 6 (CSceneStateGame::OnItemInventory,
// scenestategame.cpp:2862): ushort count, then per item int id, long uid, short slot, int rarity and the item
// (CItem::ReadFromStream, see ItemLayoutTests). Mode 8 (FUN_141715860, scenestategame.cpp:3034): int tab (a negative
// one is refused), then the same list.
public class ItemInventoryLoadLayoutTests {
    private static ItemMetadata Metadata(int id) {
        var property = new ItemMetadataProperty(false, 0, 100, 18, 0, string.Empty, string.Empty, ItemTag.None, 0, 0, 0, 0, 0, 0, 0, 0, 0, [], false, 0, false, [], [], [], 0, 0);
        var limit = new ItemMetadataLimit(Gender.All, 0, 0, 4, true, true, true, true, true, false, false, 0, [], []);
        return new ItemMetadata(id, id.ToString(), [], "", [], new ItemMetadataLife(0, 0), property, new ItemMetadataCustomize(0, 0), limit, null, null, [], null, null, null, null);
    }

    private static void AssertEntry(ByteReader reader, Item item) {
        var stream = new ByteWriter();
        stream.WriteClass<Item>(item);
        byte[] expected = stream.ToArray();
        Assert.Multiple(() => {
            Assert.That(reader.ReadInt(), Is.EqualTo(item.Id));
            Assert.That(reader.ReadLong(), Is.EqualTo(item.Uid));
            Assert.That(reader.ReadShort(), Is.EqualTo(item.Slot));
            Assert.That(reader.ReadInt(), Is.EqualTo(item.Rarity));
            Assert.That(reader.ReadBytes(expected.Length), Is.EqualTo(expected));
        });
    }

    private static Item[] Items() => [
        new Item(Metadata(20000001), 1, 5) { Uid = 0x0102030405060708, Slot = 0 },
        new Item(Metadata(20000002), 4, 1) { Uid = 0x1112131415161718, Slot = 7 },
    ];

    [Test]
    public void Load_Mode6() {
        Item[] items = Items();
        var reader = new ByteReader(ItemInventoryPacket.Load(items).ToArray(), 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.ItemInventory));
        Assert.That(reader.ReadByte(), Is.EqualTo(6));
        Assert.That((ushort) reader.ReadShort(), Is.EqualTo(2));
        AssertEntry(reader, items[0]);
        AssertEntry(reader, items[1]);
        Assert.That(reader.Available, Is.Zero);
    }

    [Test]
    public void LoadTab_Mode8() {
        Item[] items = Items();
        var reader = new ByteReader(ItemInventoryPacket.LoadTab(InventoryType.Misc, items).ToArray(), 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.ItemInventory));
        Assert.That(reader.ReadByte(), Is.EqualTo(8));
        int tab = reader.ReadInt();
        Assert.That(tab, Is.EqualTo(InventoryType.Misc.ToClient()));
        Assert.That(tab, Is.GreaterThanOrEqualTo(0));
        Assert.That((ushort) reader.ReadShort(), Is.EqualTo(2));
        AssertEntry(reader, items[0]);
        AssertEntry(reader, items[1]);
        Assert.That(reader.Available, Is.Zero);
    }
}
