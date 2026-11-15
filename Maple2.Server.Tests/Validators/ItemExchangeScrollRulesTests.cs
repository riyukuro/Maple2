using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Tests.Validators;

public class ItemExchangeScrollRulesTests {
    [Test]
    public void ExchangeBytes() {
        var writer = new ByteWriter();
        writer.WriteLong(0x1122334455667788);
        writer.WriteLong(0x0102030405060708);
        writer.WriteInt(3);
        var reader = new ByteReader(writer.ToArray(), 0);
        (long scrollUid, long targetUid, int count) = ItemExchangeScrollRules.ReadExchange(reader);
        Assert.That(scrollUid, Is.EqualTo(0x1122334455667788));
        Assert.That(targetUid, Is.EqualTo(0x0102030405060708));
        Assert.That(count, Is.EqualTo(3));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    // The client refuses only 0; a count over 1000 (enough scrolls and materials held) is read.
    [TestCase(1, true)]
    [TestCase(1000, true)]
    [TestCase(1001, true)]
    [TestCase(int.MaxValue, true)]
    [TestCase(0, false)]
    [TestCase(-1, false)]
    [TestCase(int.MinValue, false)]
    public void Count(int count, bool expected) {
        Assert.That(ItemExchangeScrollRules.CountAllowed(count), Is.EqualTo(expected));
    }

    // Every amount of one exchange times the count stays an int, so the bag's plan cannot wrap to a negative take.
    [Test]
    public void AmountsFit() {
        Assert.That(ItemExchangeScrollRules.AmountsFit([1, 100, 5], 2000), Is.True);
        Assert.That(ItemExchangeScrollRules.AmountsFit([1, int.MaxValue], 1), Is.True);
        Assert.That(ItemExchangeScrollRules.AmountsFit([1, 100_000], 30_000), Is.False);
        Assert.That(ItemExchangeScrollRules.AmountsFit([1, 2], int.MaxValue), Is.False);
        Assert.That(ItemExchangeScrollRules.AmountsFit([-1], 1), Is.False);
    }

    [Test]
    public void Fee() {
        Assert.That(ItemExchangeScrollRules.Fee(500, 3), Is.EqualTo(1500));
        Assert.That(ItemExchangeScrollRules.Fee(0, 3), Is.EqualTo(0));
        Assert.That(ItemExchangeScrollRules.Fee(500, -3), Is.EqualTo(-1));
        Assert.That(ItemExchangeScrollRules.Fee(-500, 3), Is.EqualTo(-1));
        Assert.That(ItemExchangeScrollRules.Fee(long.MaxValue / 2, 3), Is.EqualTo(-1));
    }

    // Three stacks of the scroll: the exchange takes its total once, not once per stack.
    [Test]
    public void Stacks() {
        Assert.That(ItemExchangeScrollRules.TakeFromStacks([5, 5, 5], 7), Is.EqualTo(new[] { 5, 2, 0 }));
        Assert.That(ItemExchangeScrollRules.TakeFromStacks([5, 5, 5], 15), Is.EqualTo(new[] { 5, 5, 5 }));
        Assert.That(ItemExchangeScrollRules.TakeFromStacks([5, 5, 5], 16), Is.Null);
        Assert.That(ItemExchangeScrollRules.TakeFromStacks([5], 0), Is.EqualTo(new[] { 0 }));
        Assert.That(ItemExchangeScrollRules.TakeFromStacks([5], -1), Is.Null);
    }
}
