using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager.Config;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// KeyTable 0x3A from the client. Key binds: CKeyTable::SendKeysToServer (client/ms2/keytable.cpp:407-422), byte 2,
// int count, count x (int key, int type, long code, int slot, byte priority); SendKeyTableMode1 (keytable.cpp:385)
// writes the same with byte 1 and a count of 1. Quick slots (unnamed/unit_14092f420_accountsessionstate.cpp): byte 3,
// short slot, int skill, int item, long uid, int kind (:405); byte 4, the same with -1 (:468).
public class KeyTableRequestTests {
    private static ByteReader Reader(ByteWriter writer) => new(writer.ToArray(), 0);

    [Test]
    public void KeyBindsAreTwentyOneBytesEach() {
        var writer = new ByteWriter();
        writer.WriteByte(2);
        writer.WriteInt(2);
        writer.WriteInt(0xF00501);
        writer.WriteInt(1);
        writer.WriteLong(0x0102030405060708);
        writer.WriteInt(3);
        writer.WriteByte(4);
        writer.WriteInt(0xCF);
        writer.WriteInt(5);
        writer.WriteLong(10000001);
        writer.WriteInt(-1);
        writer.WriteByte(0);

        ByteReader reader = Reader(writer);
        Assert.That(reader.ReadByte(), Is.EqualTo(2));
        System.Collections.Generic.List<KeyBind> keyBinds = KeyTableHandler.ReadKeyBinds(reader);
        Assert.That(keyBinds, Is.EqualTo(new[] {
            new KeyBind(0xF00501, 1, 0x0102030405060708, 3, 4),
            new KeyBind(0xCF, 5, 10000001, -1, 0),
        }));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void QuickSlotMoveAndAdd() {
        var writer = new ByteWriter();
        writer.WriteByte(3);
        writer.WriteShort(1);
        writer.WriteInt(10000001);
        writer.WriteInt(20000001);
        writer.WriteLong(0x0102030405060708);
        writer.WriteInt(7);

        ByteReader reader = Reader(writer);
        Assert.That(reader.ReadByte(), Is.EqualTo(3));
        Assert.That(KeyTableHandler.ReadQuickSlotMove(reader),
            Is.EqualTo(((short) 1, new QuickSlot(10000001, 20000001, 0x0102030405060708), 7)));
        Assert.That(reader.Available, Is.EqualTo(0));

        writer = new ByteWriter();
        writer.WriteByte(4);
        writer.WriteShort(2);
        writer.WriteInt(0);
        writer.WriteInt(20000001);
        writer.WriteLong(99);
        writer.WriteInt(-1);

        reader = Reader(writer);
        Assert.That(reader.ReadByte(), Is.EqualTo(4));
        Assert.That(KeyTableHandler.ReadQuickSlotMove(reader),
            Is.EqualTo(((short) 2, new QuickSlot(0, 20000001, 99), -1)));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    // The server's KeyTable 0, read as CKeyTable::OnKeyTable (keytable.cpp:285) does: byte mode, bool default, int
    // count x (int key, int type, long code, int slot, byte priority) (:313-318), short page, short pages, per page
    // int count x (int slot, int skill, int item, long uid) (:326-336).
    [Test]
    public void LoadReadsAsTheClient() {
        var bar = new HotBar([new QuickSlot(10200001, 20000001, 0x0102030405060708)]);
        ByteReader reader = Reader(KeyTablePacket.Load([new KeyBind(0x41, 2, 77, 3, 1)], 1, [bar]));
        Assert.That(reader.ReadShort(), Is.EqualTo((short) SendOp.KeyTable));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo(0x41));
        Assert.That(reader.ReadInt(), Is.EqualTo(2));
        Assert.That(reader.ReadLong(), Is.EqualTo(77));
        Assert.That(reader.ReadInt(), Is.EqualTo(3));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadShort(), Is.EqualTo((short) 1));
        Assert.That(reader.ReadShort(), Is.EqualTo((short) 1));
        int slots = reader.ReadInt();
        Assert.That(slots, Is.EqualTo(bar.Slots.Length));
        for (int i = 0; i < slots; i++) {
            Assert.That(reader.ReadInt(), Is.EqualTo(i));
            Assert.That(reader.ReadInt(), Is.EqualTo(bar.Slots[i].SkillId));
            Assert.That(reader.ReadInt(), Is.EqualTo(bar.Slots[i].ItemId));
            Assert.That(reader.ReadLong(), Is.EqualTo(bar.Slots[i].ItemUid));
        }
        Assert.That(reader.Available, Is.EqualTo(0));
    }
}
