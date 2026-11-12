using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Tests.Validators;

public class ItemBoxRulesTests {
    // RequestItemBox 0x001A: i32 item id, i16 slot, i32 count, the key string.
    [TestCase(1, 20, true)]
    [TestCase(20, 20, true)]
    [TestCase(21, 20, false)]
    [TestCase(0, 20, false)]
    [TestCase(-1, 20, false)]
    [TestCase(int.MaxValue, 20, false)]
    [TestCase(20, 0, true)] // no table row: the client's own 20
    [TestCase(21, 0, false)]
    [TestCase(30, 50, true)]
    public void OpenCount(int count, int max, bool expected) {
        var writer = new ByteWriter();
        writer.WriteInt(20300041);
        writer.WriteShort(3);
        writer.WriteInt(count);
        writer.WriteUnicodeString("");
        var packet = new ByteReader(writer.ToArray(), 0);
        Assert.That(packet.ReadInt(), Is.EqualTo(20300041));
        Assert.That(packet.ReadShort(), Is.EqualTo(3));
        Assert.That(ItemBoxRules.IsOpenCount(packet.ReadInt(), max), Is.EqualTo(expected));
        Assert.That(packet.ReadUnicodeString(), Is.EqualTo(""));
    }

    [TestCase(1, 1, false, 0, 500, ItemBoxRules.LuluPay.Box)]
    [TestCase(1, 1, true, 9000, 500, ItemBoxRules.LuluPay.Box)]
    [TestCase(1, 0, true, 500, 500, ItemBoxRules.LuluPay.Meso)]
    [TestCase(1, 0, true, 9000, 0, ItemBoxRules.LuluPay.Meso)]
    // No key: nothing, even with a box and meso. No box: only by meso, and only when ticked and enough.
    [TestCase(0, 1, true, 9000, 500, ItemBoxRules.LuluPay.None)]
    [TestCase(1, 0, false, 9000, 500, ItemBoxRules.LuluPay.None)]
    [TestCase(1, 0, true, 499, 500, ItemBoxRules.LuluPay.None)]
    [TestCase(1, 0, true, 9000, -500, ItemBoxRules.LuluPay.None)]
    public void LuluPayment_KeyThenBoxOrMeso(int keys, int boxes, bool autoPay, long meso, long price, ItemBoxRules.LuluPay expected) {
        Assert.That(ItemBoxRules.LuluPayment(keys, boxes, autoPay, meso, price), Is.EqualTo(expected));
    }
}
