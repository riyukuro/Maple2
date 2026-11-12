using System.Linq;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Tests.Validators;

public class MesoMarketRulesTests {
    // MesoMarket 0x0081 mode 7: i64 lowest, i64 highest.
    [TestCase(1L, 1L, true)]
    [TestCase(5000000L, 10000000L, true)]
    [TestCase(0L, 10000000L, false)]
    [TestCase(-5L, 10000000L, false)]
    [TestCase(10L, 9L, false)]
    public void SearchPacket(long min, long max, bool expected) {
        var writer = new ByteWriter();
        writer.WriteShort(0x0081);
        writer.WriteByte(7);
        writer.WriteLong(min);
        writer.WriteLong(max);
        var packet = new ByteReader(writer.ToArray(), 2);
        Assert.That(packet.ReadByte(), Is.EqualTo(7));
        long readMin = packet.ReadLong();
        long readMax = packet.ReadLong();
        Assert.That(packet.Available, Is.EqualTo(0));
        Assert.That(MesoMarketRules.IsSearchRange(readMin, readMax), Is.EqualTo(expected));
    }

    [TestCase(0, 5, true)]
    [TestCase(4, 5, true)]
    [TestCase(5, 5, false)]
    [TestCase(9, 5, false)]
    public void OwnListingsAtOnce(int active, int limit, bool expected) {
        Assert.That(MesoMarketRules.HasListingSlot(active, limit), Is.EqualTo(expected));
    }

    // Client: CMesoMarketMan::GetMinPrice and GetMaxPrice: average * (1.0001 -/+ rate), cut, then held between
    // the lowest and highest price.
    [TestCase(200L, 160L, 240L)]
    [TestCase(116L, 92L, 139L)]
    [TestCase(136L, 108L, 163L)]
    [TestCase(50L, 50L, 60L)]
    [TestCase(40L, 50L, 50L)]
    [TestCase(3000L, 2400L, 3000L)]
    public void PriceRange(long average, long low, long high) {
        Assert.That(MesoMarketRules.PriceRange(average, 0.2f, 50, 3000), Is.EqualTo((low, high)));
    }

    // Each sale counts once whatever its amount; fewer sales than the least count leave the fallback.
    [TestCase(new long[0], new long[0], 1, 200L)]
    [TestCase(new[] { 5000000L }, new[] { 20L }, 1, 50L)]
    [TestCase(new[] { 5000000L, 10000000L }, new[] { 160L, 280L }, 1, 150L)]
    [TestCase(new[] { 5000000L, 500000000L }, new[] { 160L, 24000L }, 1, 200L)]
    [TestCase(new[] { 5000000L, 4999999L, 5000000L }, new[] { 100L, 900L, 0L }, 1, 100L)]
    [TestCase(new[] { 5000000L }, new[] { 9000L }, 1, 3000L)]
    [TestCase(new[] { 5000000L, 5000000L }, new[] { 160L, 160L }, 3, 200L)]
    [TestCase(new[] { 5000000L, 5000000L, 5000000L }, new[] { 160L, 160L, 190L }, 3, 170L)]
    public void AveragePrice(long[] amounts, long[] prices, int minSales, long expected) {
        Assert.That(MesoMarketRules.AveragePrice(amounts.Zip(prices), 5000000, 200, 50, 3000, minSales), Is.EqualTo(expected));
    }

    // With the least count of 10 sales and a range of 20%, sales at the top of the range raise the average by no
    // more than 2% each.
    [Test]
    public void AveragePrice_MovesTwoPercentPerSaleAtMost() {
        var sold = new System.Collections.Generic.List<(long Amount, long Price)>();
        long average = 200;
        for (int i = 0; i < 60; i++) {
            (long _, long high) = MesoMarketRules.PriceRange(average, 0.2f, 50, 3000);
            sold.Insert(0, (5000000L, high));
            long next = MesoMarketRules.AveragePrice(sold.Take(100), 5000000, 200, 50, 3000, 10);
            Assert.That(next, Is.LessThanOrEqualTo(i == 9 ? 240 : average + average / 50 + 1), $"sale {i + 1}");
            average = next;
        }
    }
}
