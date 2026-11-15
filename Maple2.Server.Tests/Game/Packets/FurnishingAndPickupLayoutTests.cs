using System;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game.Packets;

// Server packets against the client's readers: each test reads the packet as the reader does
// and checks it is read to its last byte.
public class FurnishingAndPickupLayoutTests {
    private static ByteReader Reader(ByteWriter writer, SendOp op) {
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(op));
        return reader;
    }

    private static ItemMetadata Cube(int id) {
        var property = new ItemMetadataProperty(false, 0, 100, 18, 0, string.Empty, string.Empty, ItemTag.None, 0, 0, 0, 0, 0, 0, 0, 0, 0, [], false, 0, false, [], [], [], 0, 0);
        var limit = new ItemMetadataLimit(Gender.All, 0, 0, 4, true, true, true, true, true, false, false, 0, [], []);
        return new ItemMetadata(id, id.ToString(), [], "", [], new ItemMetadataLife(0, 0), property, new ItemMetadataCustomize(0, 0), limit, null, null, [], null, null, null, null);
    }

    #region FurnishingInventory (0x24)
    // CSceneStateGame::OnFurnishingInventory (scenestategame.cpp:9374): byte mode; 0 clears the list; 1 int item id,
    // long uid, long (CUGCMapItem's third long; the server writes the cube's expiry), bool look and a
    // CUgcItemLook; 2 long uid; 3 long uid and int amount. Mode 4 ends the list and reads nothing.
    [Test]
    public void Furnishing_StartAndEnd() {
        ByteReader start = Reader(FurnishingInventoryPacket.StartList(), SendOp.FurnishingInventory);
        Assert.That(start.ReadByte(), Is.Zero);
        Assert.That(start.Available, Is.Zero);
        ByteReader end = Reader(FurnishingInventoryPacket.EndList(), SendOp.FurnishingInventory);
        Assert.That(end.ReadByte(), Is.EqualTo(4));
        Assert.That(end.Available, Is.Zero);
    }

    [Test]
    public void Furnishing_Add() {
        var cube = new PlotCube(Cube(50200001), 0x0102030405060708) { Type = PlotCube.CubeType.Default, ExpiryTime = 1_700_000_000 };
        ByteReader reader = Reader(FurnishingInventoryPacket.Add(cube), SendOp.FurnishingInventory);
        Assert.Multiple(() => {
            Assert.That(reader.ReadByte(), Is.EqualTo(1));
            Assert.That(reader.ReadInt(), Is.EqualTo(50200001));
            Assert.That(reader.ReadLong(), Is.EqualTo(0x0102030405060708));
            Assert.That(reader.ReadLong(), Is.EqualTo(1_700_000_000));
            Assert.That(reader.ReadBool(), Is.False);
            Assert.That(reader.Available, Is.Zero);
        });
    }

    // The look as CUgcItemLook::Read (ugcdef.cpp:1551) takes it: long uid, two strings, byte, int, two longs, string,
    // long, string, byte.
    [Test]
    public void Furnishing_AddWithLook() {
        var look = new UgcItemLook {
            Id = 77, Name = "cube", AccountId = 11, CharacterId = 12, Author = "author", CreationTime = 13, Url = "item/ms2/01/77.m2u",
        };
        var cube = new PlotCube(Cube(50200001), 5, look) { Type = PlotCube.CubeType.Default };
        ByteReader reader = Reader(FurnishingInventoryPacket.Add(cube), SendOp.FurnishingInventory);
        Assert.Multiple(() => {
            Assert.That(reader.ReadByte(), Is.EqualTo(1));
            Assert.That(reader.ReadInt(), Is.EqualTo(50200001));
            Assert.That(reader.ReadLong(), Is.EqualTo(5));
            Assert.That(reader.ReadLong(), Is.Zero);
            Assert.That(reader.ReadBool(), Is.True);
            Assert.That(reader.ReadLong(), Is.EqualTo(77));
            reader.ReadUnicodeString(); // file name
            Assert.That(reader.ReadUnicodeString(), Is.EqualTo("cube"));
            reader.ReadByte();
            reader.ReadInt();
            Assert.That(reader.ReadLong(), Is.EqualTo(11));
            Assert.That(reader.ReadLong(), Is.EqualTo(12));
            Assert.That(reader.ReadUnicodeString(), Is.EqualTo("author"));
            Assert.That(reader.ReadLong(), Is.EqualTo(13));
            Assert.That(reader.ReadUnicodeString(), Is.EqualTo("item/ms2/01/77.m2u"));
            reader.ReadByte();
            Assert.That(reader.Available, Is.Zero);
        });
    }

    [Test]
    public void Furnishing_RemoveAndUpdate() {
        ByteReader remove = Reader(FurnishingInventoryPacket.Remove(0x1122334455667788), SendOp.FurnishingInventory);
        Assert.Multiple(() => {
            Assert.That(remove.ReadByte(), Is.EqualTo(2));
            Assert.That(remove.ReadLong(), Is.EqualTo(0x1122334455667788));
            Assert.That(remove.Available, Is.Zero);
        });
        ByteReader update = Reader(FurnishingInventoryPacket.Update(0x1122334455667788, 9), SendOp.FurnishingInventory);
        Assert.Multiple(() => {
            Assert.That(update.ReadByte(), Is.EqualTo(3));
            Assert.That(update.ReadLong(), Is.EqualTo(0x1122334455667788));
            Assert.That(update.ReadInt(), Is.EqualTo(9));
            Assert.That(update.Available, Is.Zero);
        });
    }
    #endregion

    #region FieldRemoveItem (0x2c), FieldPickupItem (0x2d)
    // CSceneStateGame::OnFieldRemoveItem (scenestategame.cpp:2402): int object id.
    [Test]
    public void RemoveItem_ObjectId() {
        ByteReader reader = Reader(FieldPacket.RemoveItem(0x0A0B0C0D), SendOp.FieldRemoveItem);
        Assert.That(reader.ReadInt(), Is.EqualTo(0x0A0B0C0D));
        Assert.That(reader.Available, Is.Zero);
    }

    // CSceneStateGame::OnFieldPickupItem (scenestategame.cpp:2420): byte picked, int drop id; picked: int owner, a
    // meso drop's long amount, a stamina drop's int amount.
    [Test]
    public void Pickup_ItemMesoStamina() {
        var test = new TestGameSession();
        test.Start();
        int owner = test.Session.Player.ObjectId;

        ByteReader item = Reader(ItemPickupPacket.PickupItem(7001, test.Session.Player), SendOp.FieldPickupItem);
        Assert.Multiple(() => {
            Assert.That(item.ReadBool(), Is.True);
            Assert.That(item.ReadInt(), Is.EqualTo(7001));
            Assert.That(item.ReadInt(), Is.EqualTo(owner));
            Assert.That(item.Available, Is.Zero);
        });
        ByteReader meso = Reader(ItemPickupPacket.PickupMeso(7002, test.Session.Player, 0x0102030405), SendOp.FieldPickupItem);
        Assert.Multiple(() => {
            Assert.That(meso.ReadBool(), Is.True);
            Assert.That(meso.ReadInt(), Is.EqualTo(7002));
            Assert.That(meso.ReadInt(), Is.EqualTo(owner));
            Assert.That(meso.ReadLong(), Is.EqualTo(0x0102030405));
            Assert.That(meso.Available, Is.Zero);
        });
        ByteReader stamina = Reader(ItemPickupPacket.PickupStamina(7003, test.Session.Player, 25), SendOp.FieldPickupItem);
        Assert.Multiple(() => {
            Assert.That(stamina.ReadBool(), Is.True);
            Assert.That(stamina.ReadInt(), Is.EqualTo(7003));
            Assert.That(stamina.ReadInt(), Is.EqualTo(owner));
            Assert.That(stamina.ReadInt(), Is.EqualTo(25));
            Assert.That(stamina.Available, Is.Zero);
        });
    }
    #endregion

    #region WorldShareInfo (0xb0)
    // CSceneStateGame::OnWorldShareInfo (scenestategame.cpp:9756): byte mode 0, then SpecialNPCInfo::Read: int npc id,
    // int field id, short channel, long time (the server writes epoch seconds), byte alive.
    [Test]
    public void WorldShareInfo_BossAlive() {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        ByteReader reader = Reader(WorldShareInfoPacket.BossAlive(23000001, 02000064, 3, now, true), SendOp.WorldShareInfo);
        Assert.Multiple(() => {
            Assert.That(reader.ReadByte(), Is.Zero);
            Assert.That(reader.ReadInt(), Is.EqualTo(23000001));
            Assert.That(reader.ReadInt(), Is.EqualTo(02000064));
            Assert.That(reader.ReadShort(), Is.EqualTo(3));
            Assert.That(reader.ReadLong(), Is.EqualTo(now));
            Assert.That(reader.ReadBool(), Is.True);
            Assert.That(reader.Available, Is.Zero);
        });
    }
    #endregion
}
