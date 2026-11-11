using System;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

public class MesoMarketCountLayoutTests {
    private static byte[] Bytes(ByteWriter writer) {
        return writer.Buffer.AsSpan(0, writer.Length).ToArray();
    }

    [Test]
    public void MesoListingCreatedEndsWithTheDailyCount() {
        var listing = new MesoListing { Id = 0x1122, CreationTime = 5, ExpiryTime = 6, Price = 7, Amount = 8 };
        byte[] bytes = Bytes(MesoMarketPacket.Create(listing, 3));
        // opcode 0x0099, mode 5, five longs, own-listing bool, int count
        Assert.That(bytes.Length, Is.EqualTo(3 + 40 + 1 + 4));
        Assert.That(bytes.AsSpan(0, 3).ToArray(), Is.EqualTo(new byte[] { 0x99, 0x00, 5 }));
        Assert.That(BitConverter.ToInt64(bytes, 3), Is.EqualTo(0x1122));
        Assert.That(BitConverter.ToInt64(bytes, 11), Is.EqualTo(8));
        Assert.That(BitConverter.ToInt64(bytes, 19), Is.EqualTo(7));
        Assert.That(BitConverter.ToInt64(bytes, 27), Is.EqualTo(5));
        Assert.That(BitConverter.ToInt64(bytes, 35), Is.EqualTo(6));
        Assert.That(bytes[43], Is.EqualTo(1));
        Assert.That(BitConverter.ToInt32(bytes, 44), Is.EqualTo(3));
    }

    [Test]
    public void MesoPurchaseEndsWithTheMonthlyCount() {
        byte[] bytes = Bytes(MesoMarketPacket.Purchase(0x3344, 4));
        // opcode 0x0099, mode 8, int code, long listing id, int count
        Assert.That(bytes.Length, Is.EqualTo(3 + 4 + 8 + 4));
        Assert.That(bytes.AsSpan(0, 3).ToArray(), Is.EqualTo(new byte[] { 0x99, 0x00, 8 }));
        Assert.That(BitConverter.ToInt32(bytes, 3), Is.EqualTo(0));
        Assert.That(BitConverter.ToInt64(bytes, 7), Is.EqualTo(0x3344));
        Assert.That(BitConverter.ToInt32(bytes, 15), Is.EqualTo(4));
    }
}
