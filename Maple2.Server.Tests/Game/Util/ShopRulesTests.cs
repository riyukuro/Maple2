using System;
using Maple2.Model.Enum;
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

    // The client's names: currency 3 is honor and 4 karma (uiworldshareinfoeventlistener.cpp:264), the shop's
    // ValorToken and Treva; the other currencies have no shop_buy condition the server raises.
    [TestCase(ShopCurrencyType.Meso, ConditionType.shop_buy)]
    [TestCase(ShopCurrencyType.Treva, ConditionType.shop_buy_karma_token)]
    [TestCase(ShopCurrencyType.ValorToken, ConditionType.shop_buy_honor_token)]
    [TestCase(ShopCurrencyType.Rue, null)]
    [TestCase(ShopCurrencyType.Item, null)]
    [TestCase(ShopCurrencyType.Meret, null)]
    public void BuyCondition_ByTheCurrencyPaid(ShopCurrencyType currency, ConditionType? expected) {
        Assert.That(ShopRules.BuyCondition(currency), Is.EqualTo(expected));
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

    [Test]
    public void Sell_ClientBytesWithANegativeQuantityAreRefused() {
        // Client sender: client/ms2/itemugcmesh.cpp:1097 (item uid, count); it sends nothing for a count below 1.
        var writer = new ByteWriter();
        writer.WriteLong(5001);
        writer.WriteInt(-1);
        var reader = new ByteReader(writer.Buffer.AsSpan(0, writer.Length).ToArray(), 0);
        Assert.That(reader.ReadLong(), Is.EqualTo(5001));
        int quantity = reader.ReadInt();
        Assert.That(ShopRules.TrySellTotal(10, quantity, 50, out long total), Is.False);
        Assert.That(total, Is.Zero);
    }

    [TestCase(10, 0, 50)]
    [TestCase(10, 51, 50)]
    [TestCase(-1, 1, 50)]
    public void TrySellTotal_Refuses(long unitPrice, int quantity, int owned) {
        Assert.That(ShopRules.TrySellTotal(unitPrice, quantity, owned, out _), Is.False);
    }

    [TestCase(10, 1, 50, 10)]
    [TestCase(10, 50, 50, 500)]
    [TestCase(3000000000, 3, 3, 9000000000)]
    public void TrySellTotal_PaysForEachItem(long unitPrice, int quantity, int owned, long expected) {
        Assert.That(ShopRules.TrySellTotal(unitPrice, quantity, owned, out long total), Is.True);
        Assert.That(total, Is.EqualTo(expected));
    }
}
