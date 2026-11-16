using Maple2.Model.Enum;
using Maple2.Model.Validators;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Tests.Game.Packets;

// ItemPutOn: OnItemPutOn (scenestategame.cpp, the record reader FUN_1407a8100): int object id, int item id, int64 uid, byte
// slot, int rarity, byte type, then the item. ItemPutOff (OnItemPutOff): int object id, int64 uid. BadgeEquip
// (OnBadgeEquip, scenestategame.cpp:11006): byte mode; 0 an int object id, int item id, int64 uid, int rarity, byte badge
// slot and the item; 1 an int object id and a byte slot; 4 a short error.
public class EquipLayoutTests {
    [Test]
    public void PutOnPutOffAndTheBadgeModes() {
        var test = new TestGameSession();
        test.Start();
        var player = test.Session.Player;
        Item item = new(TestGameSession.Metadata(11400001, EquipSlot.CL), 1) { Uid = 0x0102030405, Rarity = 3, Slot = (short) EquipSlot.CL };
        var stream = new ByteWriter();
        stream.WriteClass<Item>(item);
        byte[] body = stream.ToArray();

        var putOn = new ByteReader(EquipPacket.EquipItem(player, item, 1).ToArray(), 0);
        Assert.That(putOn.Read<SendOp>(), Is.EqualTo(SendOp.ItemPutOn));
        Assert.That(putOn.ReadInt(), Is.EqualTo(player.ObjectId));
        Assert.That(putOn.ReadInt(), Is.EqualTo(11400001));
        Assert.That(putOn.ReadLong(), Is.EqualTo(0x0102030405));
        Assert.That(putOn.ReadByte(), Is.EqualTo((byte) EquipSlot.CL));
        Assert.That(putOn.ReadInt(), Is.EqualTo(3));
        Assert.That(putOn.ReadByte(), Is.EqualTo(1));
        Assert.That(putOn.ReadBytes(body.Length), Is.EqualTo(body));
        Assert.That(putOn.Available, Is.Zero);

        var putOff = new ByteReader(EquipPacket.UnequipItem(player, item).ToArray(), 0);
        Assert.That(putOff.Read<SendOp>(), Is.EqualTo(SendOp.ItemPutOff));
        Assert.That(putOff.ReadInt(), Is.EqualTo(player.ObjectId));
        Assert.That(putOff.ReadLong(), Is.EqualTo(0x0102030405));
        Assert.That(putOff.Available, Is.Zero);

        var badge = new ByteReader(EquipPacket.EquipBadge(player, item).ToArray(), 0);
        Assert.That(badge.Read<SendOp>(), Is.EqualTo(SendOp.BadgeEquip));
        Assert.That(badge.ReadByte(), Is.Zero);
        Assert.That(badge.ReadInt(), Is.EqualTo(player.ObjectId));
        Assert.That(badge.ReadInt(), Is.EqualTo(11400001));
        Assert.That(badge.ReadLong(), Is.EqualTo(0x0102030405));
        Assert.That(badge.ReadInt(), Is.EqualTo(3));
        Assert.That(badge.ReadByte(), Is.EqualTo((byte) BadgeType.None));
        Assert.That(badge.ReadBytes(body.Length), Is.EqualTo(body));
        Assert.That(badge.Available, Is.Zero);

        var unequip = new ByteReader(EquipPacket.UnequipBadge(player, BadgeType.Transparency).ToArray(), 0);
        Assert.That(unequip.Read<SendOp>(), Is.EqualTo(SendOp.BadgeEquip));
        Assert.That(unequip.ReadByte(), Is.EqualTo(1));
        Assert.That(unequip.ReadInt(), Is.EqualTo(player.ObjectId));
        Assert.That(unequip.ReadByte(), Is.EqualTo((byte) BadgeType.Transparency));
        Assert.That(unequip.Available, Is.Zero);

        var error = new ByteReader(EquipPacket.BadgeError(BadgeEquipError.InvalidItem).ToArray(), 0);
        Assert.That(error.Read<SendOp>(), Is.EqualTo(SendOp.BadgeEquip));
        Assert.That(error.ReadByte(), Is.EqualTo(4));
        Assert.That(error.ReadShort(), Is.EqualTo((short) BadgeEquipError.InvalidItem));
        Assert.That(error.Available, Is.Zero);
    }
}
