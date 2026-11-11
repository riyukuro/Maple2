using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Tests.Validators;

public class BeautyRulesTests {
    [Test]
    public void CreateBytes() {
        var writer = new ByteWriter();
        writer.WriteByte(2);
        writer.WriteByte(1);
        writer.WriteInt(10200001);
        writer.WriteLong(77);
        writer.WriteInt(0x1234); // first field of the appearance data
        var reader = new ByteReader(writer.ToArray(), 0);
        (byte shop, bool useVoucher, int itemId, long shopValue) = BeautyRules.ReadCreate(reader);
        Assert.That(shop, Is.EqualTo(2));
        Assert.That(useVoucher, Is.True);
        Assert.That(itemId, Is.EqualTo(10200001));
        Assert.That(shopValue, Is.EqualTo(77));
        Assert.That(reader.ReadInt(), Is.EqualTo(0x1234));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void DeleteBytes() {
        var writer = new ByteWriter();
        writer.WriteLong(5000);
        writer.WriteByte(0);
        var reader = new ByteReader(writer.ToArray(), 0);
        (long uid, byte slot) = BeautyRules.ReadDelete(reader);
        Assert.That(uid, Is.EqualTo(5000));
        Assert.That(slot, Is.EqualTo(0));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void DyeItem() {
        Assert.That(BeautyRules.DyeItemMatches(11300001, 11300001), Is.True);
        Assert.That(BeautyRules.DyeItemMatches(11300001, 11300002), Is.False);
    }
}
