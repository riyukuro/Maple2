using System.Collections.Generic;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// ItemScript (0xF5) against the box dialogs' packet readers (uiitemlulluboxdialog.cpp, uiitempetboxdialog.cpp,
// uiitemgachaboxdialog.cpp, uiitemtreasuremapdialog.cpp: vfunc_79) and CSceneStateGame::OnItemScript
// (scenestategame.cpp:6829, mode 4 only): u8 mode; 0 (lullu box), 1 (pet box), 5 (gacha): int count x (int id, int
// count, int rank, bool special); 2: long uid, long cost; 3: bool ok, then int id, int rank when ok; 4: int item id.
// CheckCharNameResult against ReadCharNameResult (unit_1407a4f20_packetserializers.cpp:308): u8 failed, long uid,
// unicode name. Admin against CSceneStateGame::OnAdmin (scenestategame.cpp): u8 mode 0, u8 level.
// ItemOptionSimulator against CSceneStateGame::OnItemOptionSimulator (scenestategame.cpp:4937): u8 mode; 0 and 2: none;
// 1: int item id, int rank, int count x the item option block.
public class ItemScriptAdminNameLayoutTests {
    private static ByteReader Read(ByteWriter writer, SendOp op) {
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.That(reader.ReadShort(), Is.EqualTo((short) op));
        return reader;
    }

    private static Item Box(int id, int rarity, int amount) =>
        new(TestGameSession.Metadata(id), rarity, amount);

    private static void ReadItem(ByteReader reader, int id, int amount, int rank, bool special) {
        Assert.That(reader.ReadInt(), Is.EqualTo(id));
        Assert.That(reader.ReadInt(), Is.EqualTo(amount));
        Assert.That(reader.ReadInt(), Is.EqualTo(rank));
        Assert.That(reader.ReadBool(), Is.EqualTo(special));
    }

    [Test]
    public void ItemScript_BoxLists() {
        Item item = Box(20000001, 3, 5);

        ByteReader reader = Read(ItemScriptPacket.LulluBox(new List<Item> { item }), SendOp.ItemScript);
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        ReadItem(reader, 20000001, 5, 3, true);
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Read(ItemScriptPacket.PetBox(new List<(Item, bool)> { (item, false) }), SendOp.ItemScript);
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        ReadItem(reader, 20000001, 5, 3, false);
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Read(ItemScriptPacket.Gacha(new List<Item> { item }), SendOp.ItemScript);
        Assert.That(reader.ReadByte(), Is.EqualTo(5));
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        ReadItem(reader, 20000001, 5, 3, true);
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void ItemScript_TreasureMap() {
        ByteReader reader = Read(ItemScriptPacket.TreasureMapCost(9001, 500), SendOp.ItemScript);
        Assert.That(reader.ReadByte(), Is.EqualTo(2));
        Assert.That(reader.ReadLong(), Is.EqualTo(9001));
        Assert.That(reader.ReadLong(), Is.EqualTo(500));
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Read(ItemScriptPacket.TreasureMapResult(20000002, 4), SendOp.ItemScript);
        Assert.That(reader.ReadByte(), Is.EqualTo(3));
        Assert.That(reader.ReadBool(), Is.True);
        Assert.That(reader.ReadInt(), Is.EqualTo(20000002));
        Assert.That(reader.ReadInt(), Is.EqualTo(4));
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Read(ItemScriptPacket.TreasureMapFailed(), SendOp.ItemScript);
        Assert.That(reader.ReadByte(), Is.EqualTo(3));
        Assert.That(reader.ReadBool(), Is.False);
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Read(ItemScriptPacket.TreasureChest(20000003), SendOp.ItemScript);
        Assert.That(reader.ReadByte(), Is.EqualTo(4));
        Assert.That(reader.ReadInt(), Is.EqualTo(20000003));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void CheckCharacterNameAdminAndSimulator() {
        ByteReader reader = Read(CheckCharacterNamePacket.Result(true, "Ab", 9001), SendOp.CheckCharNameResult);
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadLong(), Is.EqualTo(9001));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Ab"));
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Read(AdminPacket.Level(3), SendOp.Admin);
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadByte(), Is.EqualTo(3));
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Read(ItemOptionSimulatorPacket.Start(), SendOp.ItemOptionSimulator);
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Read(ItemOptionSimulatorPacket.Options(20000001, 4, new List<ItemStats>()), SendOp.ItemOptionSimulator);
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo(20000001));
        Assert.That(reader.ReadInt(), Is.EqualTo(4));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Read(ItemOptionSimulatorPacket.End(), SendOp.ItemOptionSimulator);
        Assert.That(reader.ReadByte(), Is.EqualTo(2));
        Assert.That(reader.Available, Is.EqualTo(0));
    }
}
