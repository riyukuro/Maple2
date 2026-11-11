using System;
using System.Linq;
using Maple2.Model.Game;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Tests.Validators;

public class UgcRulesTests {
    private static readonly long[] Prices = Enumerable.Range(0, 24).Select(hour => 100L + hour).ToArray();

    [TestCase(20261111, 0, true)]
    [TestCase(20261111, 23, true)]
    [TestCase(20261111, 24, false)]
    [TestCase(20261111, -1, false)]
    [TestCase(20260230, 5, false)]
    [TestCase(20261301, 5, false)]
    [TestCase(0, 5, false)]
    [TestCase(-20261111, 5, false)]
    public void BannerTime(int date, int hour, bool expected) {
        Assert.That(UgcRules.BannerTimeValid(date, hour), Is.EqualTo(expected));
    }

    [Test]
    public void BannerPrice() {
        Assert.That(UgcRules.BannerPrice(Prices, 3), Is.EqualTo(103));
        Assert.That(UgcRules.BannerPrice(Prices, 24), Is.EqualTo(-1));
        Assert.That(UgcRules.BannerPrice(Prices, -1), Is.EqualTo(-1));
        Assert.That(UgcRules.BannerPrice([], 0), Is.EqualTo(-1));
    }

    [Test]
    public void BannerUploadFee() {
        Assert.That(UgcRules.BannerUploadFee(Prices, [(1, true, false, 3), (2, true, false, 4)]), Is.EqualTo(207));
        // A slot that already shows a design, a slot that is not reserved, a slot named twice, an hour
        // without a price and an empty list each refuse the whole upload.
        Assert.That(UgcRules.BannerUploadFee(Prices, [(1, true, false, 3), (2, true, true, 4)]), Is.EqualTo(-1));
        Assert.That(UgcRules.BannerUploadFee(Prices, [(1, true, false, 3), (2, false, false, 4)]), Is.EqualTo(-1));
        Assert.That(UgcRules.BannerUploadFee(Prices, [(1, true, false, 3), (1, true, false, 3)]), Is.EqualTo(-1));
        Assert.That(UgcRules.BannerUploadFee(Prices, [(1, true, false, 30)]), Is.EqualTo(-1));
        Assert.That(UgcRules.BannerUploadFee(Prices, []), Is.EqualTo(-1));
    }

    // One reservation record is 36 bytes: i64, i32, i64, i32 date, i32 hour, i64.
    [Test]
    public void ReservationBytes() {
        var writer = new ByteWriter();
        writer.WriteLong(77);
        writer.WriteInt(1);
        writer.WriteLong(5);
        writer.WriteInt(20261111);
        writer.WriteInt(13);
        writer.WriteLong(0);
        var reader = new ByteReader(writer.ToArray(), 0);
        var reservation = reader.Read<UgcBannerReservation>();
        Assert.That(reservation.Uid, Is.EqualTo(77));
        Assert.That(reservation.Date, Is.EqualTo(20261111));
        Assert.That(reservation.Hour, Is.EqualTo(13));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    // A slot is open to the character that reserved it and to nobody else. A design without a picture
    // (a failed upload) may be replaced by its own character; a finished one closes the slot.
    [TestCase(7, 0, false, 7, true)] // reserved by the sender
    [TestCase(7, 0, false, 8, false)] // reserved by another character
    [TestCase(0, 0, false, 7, false)] // owner not known, no design
    [TestCase(7, 7, false, 7, true)] // own design without a picture
    [TestCase(0, 7, false, 7, true)] // the same after a restart
    [TestCase(7, 7, true, 7, false)] // own finished design
    [TestCase(7, 7, false, 8, false)] // another character's unfinished design
    [TestCase(8, 7, false, 7, false)] // design and owner disagree
    [TestCase(0, 0, false, 0, false)]
    public void BannerSlotOwner(long owner, long design, bool picture, long sender, bool expected) {
        Assert.That(UgcRules.BannerSlotOpenTo(owner, design, picture, sender), Is.EqualTo(expected));
    }

    // An upload naming another character's slot is refused whole and costs nothing.
    [Test]
    public void BannerUploadOfOtherOwner() {
        bool own = !UgcRules.BannerSlotOpenTo(7, 0, false, 7);
        bool other = !UgcRules.BannerSlotOpenTo(8, 0, false, 7);
        Assert.That(UgcRules.BannerUploadFee(Prices, [(1, true, own, 3)]), Is.EqualTo(103));
        Assert.That(UgcRules.BannerUploadFee(Prices, [(1, true, own, 3), (2, true, other, 4)]), Is.EqualTo(-1));
    }

    [TestCase(0, 24)]
    [TestCase(20, 4)]
    [TestCase(24, 0)]
    [TestCase(30, 0)]
    [TestCase(-5, 24)]
    public void BannerReserveRoom(int unpaid, int expected) {
        Assert.That(UgcRules.BannerReserveRoom(unpaid), Is.EqualTo(expected));
    }

    [Test]
    public void BannerTimeOpen() {
        var now = new DateTimeOffset(2026, 11, 11, 13, 30, 0, TimeSpan.Zero);
        Assert.That(UgcRules.BannerTimeOpen(20261111, 13, now), Is.True);
        Assert.That(UgcRules.BannerTimeOpen(20261111, 12, now), Is.False);
        Assert.That(UgcRules.BannerTimeOpen(20261110, 23, now), Is.False);
        Assert.That(UgcRules.BannerTimeOpen(20261112, 0, now), Is.True);
        Assert.That(UgcRules.BannerTimeOpen(20260230, 5, now), Is.False);
    }
}
