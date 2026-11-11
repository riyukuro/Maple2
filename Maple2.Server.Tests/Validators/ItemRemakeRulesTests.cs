using System.Collections.Generic;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Tests.Validators;

public class ItemRemakeRulesTests {
    // Mode 0 of 0x0075: i64 item, i64 ingredient, then the kept options.
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(3)]
    public void ChangeBytes(int count) {
        var writer = new ByteWriter();
        writer.WriteLong(9001);
        writer.WriteLong(9002);
        writer.WriteByte((byte) count);
        for (int i = 0; i < count; i++) {
            writer.WriteByte((byte) (i % 2));
            writer.WriteShort((short) (20 + i));
        }
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.That(reader.ReadLong(), Is.EqualTo(9001));
        Assert.That(reader.ReadLong(), Is.EqualTo(9002));
        List<ItemRemakeRules.Lock> locks = ItemRemakeRules.ReadLocks(reader);
        Assert.That(locks, Has.Count.EqualTo(count));
        for (int i = 0; i < count; i++) {
            Assert.That(locks[i], Is.EqualTo(new ItemRemakeRules.Lock(i % 2 == 1, (short) (20 + i))));
        }
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    // Mode 1 of 0x0076: i64 scroll, i64 item, i64 ingredient, u8 kept-lock flag, then the kept options.
    [Test]
    public void ScrollBytes() {
        var writer = new ByteWriter();
        writer.WriteLong(1);
        writer.WriteLong(2);
        writer.WriteLong(3);
        writer.WriteBool(true);
        writer.WriteByte(2);
        writer.WriteByte(1);
        writer.WriteShort(7);
        writer.WriteByte(0);
        writer.WriteShort(8);
        var reader = new ByteReader(writer.ToArray(), 0);
        reader.ReadLong();
        reader.ReadLong();
        reader.ReadLong();
        Assert.That(reader.ReadBool(), Is.True);
        List<ItemRemakeRules.Lock> locks = ItemRemakeRules.ReadLocks(reader);
        Assert.That(locks, Is.EqualTo(new[] { new ItemRemakeRules.Lock(true, 7), new ItemRemakeRules.Lock(false, 8) }));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void Allowed() {
        Assert.That(ItemRemakeRules.LocksAllowed(0), Is.True);
        Assert.That(ItemRemakeRules.LocksAllowed(1), Is.True);
        Assert.That(ItemRemakeRules.LocksAllowed(2), Is.False);
    }
}
