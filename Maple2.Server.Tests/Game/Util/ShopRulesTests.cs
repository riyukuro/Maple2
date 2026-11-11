using System;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

// The bytes are the client's sender: the shop dialog's purchase request (client/ms2/uishopdialog.cpp:2846),
// after the sub-type: shop item id, quantity, shop id.
public class ShopRulesTests {
    [Test]
    public void BuyRequest_QuantityIsTheSecondInt() {
        var writer = new ByteWriter();
        writer.WriteInt(1001);
        writer.WriteInt(-5);
        writer.WriteInt(160);
        var reader = new ByteReader(writer.Buffer.AsSpan(0, writer.Length).ToArray(), 0);

        Assert.That(reader.ReadInt(), Is.EqualTo(1001));
        int quantity = reader.ReadInt();
        Assert.That(quantity, Is.EqualTo(-5));
        Assert.That(ShopRules.TryTotalPrice(100, quantity, out _), Is.False);
    }

    [TestCase(100, 0)]
    [TestCase(100, -1)]
    [TestCase(100, int.MinValue)]
    [TestCase(100000, 30000)]
    [TestCase(int.MaxValue, 2)]
    [TestCase(-1, 1)]
    public void TryTotalPrice_RefusesWhatTheClientNeverSends(int unitPrice, int quantity) {
        Assert.That(ShopRules.TryTotalPrice(unitPrice, quantity, out int total), Is.False);
        Assert.That(total, Is.Zero);
    }

    [TestCase(100, 1, 100)]
    [TestCase(0, 5, 0)]
    [TestCase(21474836, 100, 2147483600)]
    public void TryTotalPrice_MultipliesInRange(int unitPrice, int quantity, int expected) {
        Assert.That(ShopRules.TryTotalPrice(unitPrice, quantity, out int total), Is.True);
        Assert.That(total, Is.EqualTo(expected));
    }
}
