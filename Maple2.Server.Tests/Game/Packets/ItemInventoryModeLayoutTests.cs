using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Tests.Game.Packets;

// The ItemInventory modes other than the lists, read as CSceneStateGame::OnItemInventory (scenestategame.cpp:2862)
// reads them: 0 an int id, int64 uid, short slot, int rarity and the item; 1 an int64 uid; 2 an int64 uid and an int
// amount; 3 two (int64 uid, short slot) pairs; 7 an int64 uid, an int and a unicode string; 10 nothing; 0xb an int tab;
// 0xc a byte tab and an int; 0xd an int error and, for the two level errors, an int item id and a short level; 0xe an
// int64 uid and the item. ItemUpdate (OnItemUpdate): an int player id, an int64 uid and the item.
public class ItemInventoryModeLayoutTests {
    private static Item Item() {
        var property = new ItemMetadataProperty(false, 0, 100, 18, 0, string.Empty, string.Empty, ItemTag.None, 0, 0, 0, 0, 0, 0, 0, 0, 0, [], false, 0, false, [], [], [], 0, 0);
        var limit = new ItemMetadataLimit(Gender.All, 0, 0, 4, true, true, true, true, true, false, false, 0, [], []);
        var metadata = new ItemMetadata(20000001, "20000001", [], "", [], new ItemMetadataLife(0, 0), property, new ItemMetadataCustomize(0, 0), limit, null, null, [], null, null, null, null);
        return new Item(metadata, 3, 5) { Uid = 0x0102030405060708, Slot = 7, Rarity = 4 };
    }

    private static byte[] Body(Item item) {
        var stream = new ByteWriter();
        stream.WriteClass<Maple2.Model.Game.Item>(item);
        return stream.ToArray();
    }

    private static ByteReader Open(ByteWriter writer, byte mode) {
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.ItemInventory));
        Assert.That(reader.ReadByte(), Is.EqualTo(mode));
        return reader;
    }

    [Test]
    public void AddAndUpdateItem_CarryTheItem() {
        Item item = Item();
        ByteReader add = Open(ItemInventoryPacket.Add(item), 0);
        Assert.That(add.ReadInt(), Is.EqualTo(item.Id));
        Assert.That(add.ReadLong(), Is.EqualTo(item.Uid));
        Assert.That(add.ReadShort(), Is.EqualTo(item.Slot));
        Assert.That(add.ReadInt(), Is.EqualTo(item.Rarity));
        Assert.That(add.ReadBytes(Body(item).Length), Is.EqualTo(Body(item)));
        Assert.That(add.Available, Is.Zero);

        ByteReader update = Open(ItemInventoryPacket.UpdateItem(item), 14);
        Assert.That(update.ReadLong(), Is.EqualTo(item.Uid));
        Assert.That(update.ReadBytes(Body(item).Length), Is.EqualTo(Body(item)));
        Assert.That(update.Available, Is.Zero);
    }

    [Test]
    public void RemoveAmountMoveAndNotify() {
        ByteReader remove = Open(ItemInventoryPacket.Remove(0x0102030405), 1);
        Assert.That(remove.ReadLong(), Is.EqualTo(0x0102030405));
        Assert.That(remove.Available, Is.Zero);

        ByteReader amount = Open(ItemInventoryPacket.UpdateAmount(0x0102030405, 9), 2);
        Assert.That(amount.ReadLong(), Is.EqualTo(0x0102030405));
        Assert.That(amount.ReadInt(), Is.EqualTo(9));
        Assert.That(amount.Available, Is.Zero);

        ByteReader move = Open(ItemInventoryPacket.Move(11, 2, 12, 3), 3);
        Assert.That((move.ReadLong(), move.ReadShort(), move.ReadLong(), move.ReadShort()), Is.EqualTo((11L, (short) 2, 12L, (short) 3)));
        Assert.That(move.Available, Is.Zero);

        ByteReader notify = Open(ItemInventoryPacket.NotifyNew(0x0102030405, 4), 7);
        Assert.That(notify.ReadLong(), Is.EqualTo(0x0102030405));
        Assert.That(notify.ReadInt(), Is.EqualTo(4));
        Assert.That(notify.ReadUnicodeString(), Is.Empty);
        Assert.That(notify.Available, Is.Zero);
    }

    [Test]
    public void TabModes() {
        Assert.That(Open(ItemInventoryPacket.ExpandComplete(), 10).Available, Is.Zero);

        ByteReader reset = Open(ItemInventoryPacket.Reset(InventoryType.Consumable), 11);
        Assert.That(reset.ReadInt(), Is.EqualTo(InventoryType.Consumable.ToClient()));
        Assert.That(reset.Available, Is.Zero);

        ByteReader expand = Open(ItemInventoryPacket.ExpandCount(InventoryType.Gear, 6), 12);
        Assert.That(expand.ReadByte(), Is.EqualTo(InventoryType.Gear.ToClient()));
        Assert.That(expand.ReadInt(), Is.EqualTo(6));
        Assert.That(expand.Available, Is.Zero);
    }

    [Test]
    public void Errors() {
        ByteReader error = Open(ItemInventoryPacket.Error(ItemInventoryError.s_err_inventory), 13);
        Assert.That(error.ReadInt(), Is.EqualTo((int) ItemInventoryError.s_err_inventory));
        Assert.That(error.Available, Is.Zero);

        foreach (bool pickup in new[] { false, true }) {
            ByteReader level = Open(ItemInventoryPacket.ErrorRestrictedByLevel(pickup, 20000001, 55), 13);
            Assert.That(level.ReadInt(), Is.EqualTo((int) (pickup ? ItemInventoryError.s_itempickup_error_restricted_by_user_level_ex : ItemInventoryError.s_itemdrop_error_restricted_by_user_level_ex)));
            Assert.That(level.ReadInt(), Is.EqualTo(20000001));
            Assert.That(level.ReadShort(), Is.EqualTo(55));
            Assert.That(level.Available, Is.Zero);
        }
    }

    [Test]
    public void ItemUpdate_IsPlayerUidAndItem() {
        var test = new TestGameSession();
        test.Start();
        Item item = Item();
        var reader = new ByteReader(ItemUpdatePacket.Update(test.Session.Player, item).ToArray(), 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.ItemUpdate));
        Assert.That(reader.ReadInt(), Is.EqualTo(test.Session.Player.ObjectId));
        Assert.That(reader.ReadLong(), Is.EqualTo(item.Uid));
        Assert.That(reader.ReadBytes(Body(item).Length), Is.EqualTo(Body(item)));
        Assert.That(reader.Available, Is.Zero);
    }
}
