using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Tests.Validators;

public class ItemEnchantRulesTests {
    [TestCase(true, 0, 0, true)]
    [TestCase(true, 14, 0, true)]
    [TestCase(true, 15, 0, false)]
    [TestCase(true, 16, 0, false)]
    [TestCase(true, -1, 0, false)]
    [TestCase(true, 3, 1, false)]
    [TestCase(false, 0, 0, false)]
    public void CanEnchant(bool enable, int enchants, int limitBreak, bool expected) {
        Assert.That(ItemEnchantRules.CanEnchant(enable, enchants, limitBreak), Is.EqualTo(expected));
    }

    // Mode 1: u8 kind, i64 item uid. Mode 4: i64 item uid, i64 (unused by the server).
    [Test]
    public void Bytes() {
        var writer = new ByteWriter();
        writer.WriteByte(1);
        writer.WriteLong(0x1122334455667788);
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadLong(), Is.EqualTo(0x1122334455667788));
        Assert.That(reader.Available, Is.EqualTo(0));
    }
}
