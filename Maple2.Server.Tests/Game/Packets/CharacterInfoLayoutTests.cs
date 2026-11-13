using System;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Tests.Game.Packets;

// CharacterInfo (0x78) Load: UIPCInfoDialog::vfunc_79 (uipcinfodialog.cpp:505) reads long character id, u8 result, then
// CPCDataPack::Read (pcdata.cpp:181): 3 longs and 3 length-prefixed buffers. The first buffer is CPCData::Read
// (pcdata.cpp:101), the second and third CPCDataPack::ReadItems (pcdata.cpp:259). Each buffer must be read to its end.
public class CharacterInfoLayoutTests {
    private static ByteReader Buffer(ByteReader reader) {
        int length = reader.ReadInt();
        return new ByteReader(reader.ReadBytes(length), 0);
    }

    // CPCData::Read; returns the name and the level.
    private static (string Name, short Level) ReadDetails(ByteReader reader) {
        reader.ReadLong();
        reader.ReadLong();
        string name = reader.ReadUnicodeString();
        short level = reader.ReadShort();
        for (int i = 0; i < 4; i++) reader.ReadInt();
        reader.ReadByte();
        reader.ReadBytes(3 * 0x118);
        int pairs = reader.ReadInt();
        for (int i = 0; i < pairs; i++) {
            reader.ReadShort();
            reader.ReadInt();
        }
        for (int i = 0; i < 5; i++) reader.ReadUnicodeString();
        for (int i = 0; i < 4; i++) reader.ReadInt();
        int titles = reader.ReadInt();
        for (int i = 0; i < titles; i++) reader.ReadInt();
        reader.ReadInt();
        reader.ReadInt();
        reader.ReadLong();
        reader.ReadInt();
        reader.ReadInt();
        reader.ReadInt();
        reader.ReadInt();
        reader.ReadShort();
        reader.ReadUnicodeString();
        reader.ReadUnicodeString();
        reader.ReadLong();
        reader.ReadLong();
        return (name, level);
    }

    // One ReadItems entry header (i32 id, i64 uid, u8 slot, i32 rarity); the item itself is CItem::ReadFromStream,
    // skipped here by the length the server's item writer gives.
    private static (int Id, long Uid, byte Slot) ReadEquip(ByteReader reader, Item item) {
        int id = reader.ReadInt();
        long uid = reader.ReadLong();
        byte slot = reader.ReadByte();
        reader.ReadInt();
        var itemBytes = new ByteWriter();
        itemBytes.WriteClass<Item>(item);
        reader.ReadBytes(itemBytes.Length);
        return (id, uid, slot);
    }

    [Test]
    public void Load_ReadsEachBufferToItsEnd() {
        var test = new TestGameSession();
        Item stored = test.Store(TestGameSession.Metadata(13000001, slot: EquipSlot.RH, type: 1), group: ItemGroup.Gear, slot: EquipSlot.RH);
        GameSession session = test.Start();
        Item worn = session.Item.Equips.Gear[EquipSlot.RH];

        var reader = new ByteReader(PlayerInfoPacket.Load(session).ToArray(), 0);
        Assert.That(reader.ReadShort(), Is.EqualTo((short) SendOp.CharacterInfo));
        Assert.That(reader.ReadLong(), Is.EqualTo(session.CharacterId));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        reader.ReadLong();
        reader.ReadLong();
        reader.ReadLong();

        ByteReader details = Buffer(reader);
        Assert.That(ReadDetails(details).Name, Is.EqualTo(session.Player.Value.Character.Name));
        Assert.That(details.Available, Is.EqualTo(0));

        ByteReader items = Buffer(reader);
        Assert.That(items.ReadByte(), Is.EqualTo(1));
        Assert.That(ReadEquip(items, worn), Is.EqualTo((stored.Id, stored.Uid, (byte) EquipSlot.RH)));
        items.ReadBool();
        items.ReadLong();
        items.ReadLong();
        Assert.That(items.ReadByte(), Is.EqualTo(0));
        Assert.That(items.Available, Is.EqualTo(0));

        ByteReader badges = Buffer(reader);
        Assert.That(badges.ReadByte(), Is.EqualTo(0));
        Assert.That(badges.Available, Is.EqualTo(0));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    // The third buffer's entry: u8 badge type, i32 id, i64 uid, i32 rarity, then the item.
    [Test]
    public void Badge_EntryHeader() {
        var badge = new Item(TestGameSession.Metadata(70100000, slot: EquipSlot.Unknown, type: 1)) { Badge = new ItemBadge(70100000) };
        var writer = new ByteWriter();
        Maple2.Server.Core.Packets.Helper.EquipPacketHelper.WriteBadge(writer, badge);
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.That(reader.ReadByte(), Is.EqualTo((byte) badge.Badge!.Type));
        Assert.That(reader.ReadInt(), Is.EqualTo(70100000));
        reader.ReadLong();
        reader.ReadInt();
        var itemBytes = new ByteWriter();
        itemBytes.WriteClass<Item>(badge);
        Assert.That(reader.Available, Is.EqualTo(itemBytes.Length));
    }
}
