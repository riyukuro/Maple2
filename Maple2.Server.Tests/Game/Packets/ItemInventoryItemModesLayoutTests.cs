using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Tests.Game.Packets;

// ItemInventory modes that carry an item against CSceneStateGame::OnItemInventory (scenestategame.cpp:2806): u8 mode;
// 0 (add): int item id, long uid, u16 slot, int rank, the item stream (CItem::ReadFromStream); 7 (new item notice):
// long uid, int, unicode string; 14 (update): long uid, the item stream.
// ItemSocketSystem 11 (unequip a gem) against OnItemSocketSystem (scenestategame.cpp:4837, FUN_140628be0 in
// itemsocketsystemutil.cpp:703): u8 mode, long item uid, u8 slot, long gem uid. The stream is read back against
// CItem::ReadFromStream in ItemLayoutTests, so it is compared here as the shared Item writer's bytes.
public class ItemInventoryItemModesLayoutTests {
    private static ByteReader Read(ByteWriter writer) {
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.That(reader.ReadShort(), Is.EqualTo((short) SendOp.ItemInventory));
        return reader;
    }

    private static Item Gear() => new(TestGameSession.Metadata(11300001), 4, 6) { Uid = 8001, Slot = 12 };

    private static byte[] Stream() {
        var writer = new ByteWriter();
        writer.WriteClass<Item>(Gear());
        return writer.ToArray();
    }

    [Test]
    public void Add() {
        ByteReader reader = Read(ItemInventoryPacket.Add(Gear()));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(11300001));
        Assert.That(reader.ReadLong(), Is.EqualTo(8001));
        Assert.That(reader.ReadShort(), Is.EqualTo(12));
        Assert.That(reader.ReadInt(), Is.EqualTo(4));
        Assert.That(reader.ReadBytes(reader.Available), Is.EqualTo(Stream()));
    }

    [Test]
    public void NotifyNewAndUpdate() {
        ByteReader reader = Read(ItemInventoryPacket.NotifyNew(8001, 6));
        Assert.That(reader.ReadByte(), Is.EqualTo(7));
        Assert.That(reader.ReadLong(), Is.EqualTo(8001));
        Assert.That(reader.ReadInt(), Is.EqualTo(6));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo(string.Empty));
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Read(ItemInventoryPacket.UpdateItem(Gear()));
        Assert.That(reader.ReadByte(), Is.EqualTo(14));
        Assert.That(reader.ReadLong(), Is.EqualTo(8001));
        Assert.That(reader.ReadBytes(reader.Available), Is.EqualTo(Stream()));
    }

    [Test]
    public void UnequipGemstone() {
        var reader = new ByteReader(ItemSocketPacket.UnequipGemstone(8001, 2, 9002).ToArray(), 0);
        Assert.That(reader.ReadShort(), Is.EqualTo((short) SendOp.ItemSocketSystem));
        Assert.That(reader.ReadByte(), Is.EqualTo(11));
        Assert.That(reader.ReadLong(), Is.EqualTo(8001));
        Assert.That(reader.ReadByte(), Is.EqualTo(2));
        Assert.That(reader.ReadLong(), Is.EqualTo(9002));
        Assert.That(reader.Available, Is.EqualTo(0));
    }
}
