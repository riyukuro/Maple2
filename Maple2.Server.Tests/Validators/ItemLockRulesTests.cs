using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Tests.Validators;

public class ItemLockRulesTests {
    // RequestItemLock 0x0087 mode 1: u8 mode (0 lock, 1 unlock), i64 item uid.
    [TestCase((byte) 0, true, false, false, true)]
    [TestCase((byte) 0, true, true, false, false)] // already locked
    [TestCase((byte) 1, true, true, false, true)]
    [TestCase((byte) 1, true, false, false, false)] // not locked
    [TestCase((byte) 0, false, false, false, false)] // not an own item
    [TestCase((byte) 0, true, false, true, false)] // already in the window
    public void StagePacket(byte mode, bool owned, bool locked, bool staged, bool expected) {
        var writer = new ByteWriter();
        writer.WriteShort(0x0087);
        writer.WriteByte(1);
        writer.WriteByte(mode);
        writer.WriteLong(123456789);
        var packet = new ByteReader(writer.ToArray(), 2);
        Assert.That(packet.ReadByte(), Is.EqualTo(1));
        bool unlock = packet.ReadBool();
        Assert.That(packet.ReadLong(), Is.EqualTo(123456789));
        Assert.That(packet.Available, Is.EqualTo(0));
        Assert.That(ItemLockRules.CanStage(unlock, owned, locked, staged), Is.EqualTo(expected));
    }

    // CItem::CanBeItemLocked's type list.
    [TestCase(1, true)]
    [TestCase(5, true)]
    [TestCase(9, true)]
    [TestCase(11, true)]
    [TestCase(12, true)]
    [TestCase(13, true)]
    [TestCase(21, true)]
    [TestCase(0, false)]
    [TestCase(2, false)]
    [TestCase(6, false)]
    [TestCase(7, false)]
    [TestCase(20, false)]
    public void LockableTypes(int type, bool expected) {
        Assert.That(ItemLockRules.IsLockableType(type), Is.EqualTo(expected));
    }

    [TestCase(false, false, true)]
    [TestCase(true, false, false)]
    [TestCase(false, true, false)]
    public void LimitAllowsLock(bool vip, bool wedding, bool expected) {
        Assert.That(ItemLockRules.LimitAllowsLock(vip, wedding), Is.EqualTo(expected));
    }
}
