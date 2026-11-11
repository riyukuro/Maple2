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
}
