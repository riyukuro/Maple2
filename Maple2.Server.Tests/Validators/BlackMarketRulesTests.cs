using Maple2.Model.Enum;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Tests.Validators;

public class BlackMarketRulesTests {
    // BlackMarket 0x005F mode 2: i64 item uid, i64 price, i32 count.
    [TestCase(100L, 3, 10, true)]
    [TestCase(100L, 10, 10, true)]
    [TestCase(100L, 11, 10, false)]
    [TestCase(100L, 0, 10, false)]
    [TestCase(100L, -1, 10, false)] // a negative count made the deposit negative
    [TestCase(0L, 1, 10, false)]
    [TestCase(-100L, 1, 10, false)] // a negative price paid the buyer
    [TestCase(long.MaxValue, 2, 10, false)]
    public void AddPacket(long price, int count, int owned, bool expected) {
        var writer = new ByteWriter();
        writer.WriteShort(0x005F);
        writer.WriteByte(2);
        writer.WriteLong(77);
        writer.WriteLong(price);
        writer.WriteInt(count);
        var packet = new ByteReader(writer.ToArray(), 2);
        Assert.That(packet.ReadByte(), Is.EqualTo(2));
        Assert.That(packet.ReadLong(), Is.EqualTo(77));
        long readPrice = packet.ReadLong();
        int readCount = packet.ReadInt();
        Assert.That(packet.Available, Is.EqualTo(0));
        bool ok = BlackMarketRules.IsSaleCount(readCount, owned)
                  && BlackMarketRules.IsSalePrice(readPrice, 0, 0)
                  && BlackMarketRules.IsSaleTotal(readPrice, readCount);
        Assert.That(ok, Is.EqualTo(expected));
    }

    [TestCase(50L, 100L, 1000L, false)]
    [TestCase(100L, 100L, 1000L, true)]
    [TestCase(1000L, 100L, 1000L, true)]
    [TestCase(1001L, 100L, 1000L, false)]
    public void PriceBounds(long price, long min, long max, bool expected) {
        Assert.That(BlackMarketRules.IsSalePrice(price, min, max), Is.EqualTo(expected));
    }

    [Test]
    public void LockedAndBoundItemsAreNotListed() {
        Assert.That(BlackMarketRules.CanList(false, true, TransferFlag.Trade, 0, false), Is.True);
        Assert.That(BlackMarketRules.CanList(true, true, TransferFlag.Trade, 0, false), Is.False);
        Assert.That(BlackMarketRules.CanList(false, true, TransferFlag.Trade, 0, true), Is.False);
        Assert.That(BlackMarketRules.CanList(false, true, TransferFlag.None, 0, false), Is.False);
        Assert.That(BlackMarketRules.CanList(false, true, TransferFlag.LimitTrade, 1, false), Is.True);
        Assert.That(BlackMarketRules.CanList(false, false, TransferFlag.None, 0, false), Is.True);
    }

    // BlackMarket 0x005F mode 5: i64 listing id, i32 count.
    [TestCase(1, 5, true)]
    [TestCase(5, 5, true)]
    [TestCase(6, 5, false)]
    [TestCase(0, 5, false)]
    [TestCase(-3, 5, false)] // a negative count paid the buyer
    public void PurchasePacket(int count, int listed, bool expected) {
        var writer = new ByteWriter();
        writer.WriteShort(0x005F);
        writer.WriteByte(5);
        writer.WriteLong(9001);
        writer.WriteInt(count);
        var packet = new ByteReader(writer.ToArray(), 2);
        Assert.That(packet.ReadByte(), Is.EqualTo(5));
        Assert.That(packet.ReadLong(), Is.EqualTo(9001));
        int readCount = packet.ReadInt();
        Assert.That(packet.Available, Is.EqualTo(0));
        Assert.That(BlackMarketRules.IsPurchaseCount(readCount, listed), Is.EqualTo(expected));
    }

    [Test]
    public void OwnAccountListing() {
        Assert.That(BlackMarketRules.IsOwnListing(4, 4), Is.True);
        Assert.That(BlackMarketRules.IsOwnListing(4, 5), Is.False);
    }
}
