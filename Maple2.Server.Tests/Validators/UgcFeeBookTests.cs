using Maple2.Model.Enum;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Tests.Validators;

public class UgcFeeBookTests {
    private static UgcPendingFee Fee(long character, long ugcUid, long amount = 300) {
        return new UgcPendingFee(character, ugcUid, MeretMarketCurrencyType.Meret, amount, 0);
    }

    // Mode 0 is one byte.
    [Test]
    public void FailedBytes() {
        var writer = new ByteWriter();
        writer.WriteByte(0);
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void RefundOnce() {
        var book = new UgcFeeBook();
        book.Paid(Fee(7, 100));
        Assert.That(book.TakeBack(7), Is.EqualTo(Fee(7, 100)));
        Assert.That(book.TakeBack(7), Is.Null);
    }

    [Test]
    public void NothingPending() {
        Assert.That(new UgcFeeBook().TakeBack(7), Is.Null);
    }

    [Test]
    public void OtherCharacter() {
        var book = new UgcFeeBook();
        book.Paid(Fee(7, 100));
        Assert.That(book.TakeBack(8), Is.Null);
        Assert.That(book.Finish(8, 100), Is.Null);
        Assert.That(book.TakeBack(7), Is.Not.Null);
    }

    [Test]
    public void FinishedIsNotRefunded() {
        var book = new UgcFeeBook();
        book.Paid(Fee(7, 100));
        Assert.That(book.Finish(7, 101), Is.Null);
        Assert.That(book.Finish(7, 100), Is.Not.Null);
        Assert.That(book.Finish(7, 100), Is.Null);
        Assert.That(book.TakeBack(7), Is.Null);
    }

    // A failed upload and its retry cost the fee once, with a mode 0 in between or without one.
    [TestCase(true)]
    [TestCase(false)]
    public void RetryPaidOnce(bool failureSent) {
        var book = new UgcFeeBook();
        long meret = 1000;

        meret += book.TakeBack(7)?.Amount ?? 0; // mode 1
        meret -= 300;
        book.Paid(Fee(7, 100));
        if (failureSent) {
            meret += book.TakeBack(7)?.Amount ?? 0; // mode 0
            Assert.That(meret, Is.EqualTo(1000));
            meret += book.TakeBack(7)?.Amount ?? 0; // a second mode 0
        }
        meret += book.TakeBack(7)?.Amount ?? 0; // mode 1 again
        meret -= 300;
        book.Paid(Fee(7, 101));
        Assert.That(book.Finish(7, 101), Is.Not.Null); // mode 3
        meret += book.TakeBack(7)?.Amount ?? 0; // a late mode 0

        Assert.That(meret, Is.EqualTo(700));
    }
}
