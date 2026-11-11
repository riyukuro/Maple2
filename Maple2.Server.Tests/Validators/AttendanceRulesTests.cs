using System;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Tests.Validators;

public class AttendanceRulesTests {
    [Test]
    public void EarlyParticipationBytes() {
        var writer = new ByteWriter();
        writer.WriteInt(1234);
        writer.WriteLong(0x1122334455667788);
        var reader = new ByteReader(writer.ToArray(), 0);
        (int eventId, long couponUid) = AttendanceRules.ReadEarlyParticipation(reader);
        Assert.That(eventId, Is.EqualTo(1234));
        Assert.That(couponUid, Is.EqualTo(0x1122334455667788));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void SelectedEventBytes() {
        var writer = new ByteWriter();
        writer.WriteInt(77);
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.That(AttendanceRules.ReadSelectedEvent(reader), Is.EqualTo(77));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    // Three rewards: claims 1 to 3 give rewards 0 to 2, a fourth claim gives nothing.
    [TestCase(0, 3, 0)]
    [TestCase(1, 3, 1)]
    [TestCase(2, 3, 2)]
    [TestCase(3, 3, -1)]
    [TestCase(-1, 3, -1)]
    [TestCase(0, 0, -1)]
    public void RewardIndex(int claimed, int count, int expected) {
        Assert.That(AttendanceRules.RewardIndex(claimed, count), Is.EqualTo(expected));
    }

    // A made-up payAttend row.
    [Test]
    public void ParsePay() {
        AttendGift.PayAttend? pay = AttendanceRules.ParsePay("meso", "5000", "3", "20000001");
        Assert.That(pay, Is.EqualTo(new AttendGift.PayAttend(AttendGiftCurrencyType.Meso, 5000, 3, 20000001)));
        Assert.That(AttendanceRules.ParsePay("merat", "10", "1", null), Is.EqualTo(new AttendGift.PayAttend(AttendGiftCurrencyType.Meret, 10, 1, 0)));
        Assert.That(AttendanceRules.ParsePay("gold", "10", "1", "0"), Is.Null);
        Assert.That(AttendanceRules.ParsePay("meso", "x", "1", "0"), Is.Null);
        Assert.That(AttendanceRules.ParsePay("meso", "10", "0", "0"), Is.Null);
        Assert.That(AttendanceRules.ParsePay(null, null, null, null), Is.Null);
    }

    [Test]
    public void PayCheck() {
        var pay = new AttendGift.PayAttend(AttendGiftCurrencyType.Meso, 5000, 3, 20000001);
        Assert.That(AttendanceRules.PayCheck(null, 0, 7, 0, false, 0, 9999, 9999), Is.EqualTo(AttendanceRules.PayStep.Refuse));
        Assert.That(AttendanceRules.PayCheck(pay, 7, 7, 0, false, 0, 9999, 9999), Is.EqualTo(AttendanceRules.PayStep.Refuse));
        Assert.That(AttendanceRules.PayCheck(pay, 0, 7, 3, false, 0, 9999, 9999), Is.EqualTo(AttendanceRules.PayStep.Refuse));
        Assert.That(AttendanceRules.PayCheck(pay, 0, 7, 2, false, 0, 5000, 0), Is.EqualTo(AttendanceRules.PayStep.Meso));
        Assert.That(AttendanceRules.PayCheck(pay, 0, 7, 2, false, 0, 4999, 9999), Is.EqualTo(AttendanceRules.PayStep.NoMeso));
        Assert.That(AttendanceRules.PayCheck(pay, 0, 7, 0, true, 20000001, 0, 0), Is.EqualTo(AttendanceRules.PayStep.Coupon));
        Assert.That(AttendanceRules.PayCheck(pay, 0, 7, 0, true, 20000002, 9999, 9999), Is.EqualTo(AttendanceRules.PayStep.NoCoupon));
        Assert.That(AttendanceRules.PayCheck(pay, 0, 7, 0, true, 0, 9999, 9999), Is.EqualTo(AttendanceRules.PayStep.NoCoupon));
        AttendGift.PayAttend meret = pay with { Type = AttendGiftCurrencyType.Meret, CouponItemId = 0 };
        Assert.That(AttendanceRules.PayCheck(meret, 0, 7, 0, false, 0, 0, 5000), Is.EqualTo(AttendanceRules.PayStep.Meret));
        Assert.That(AttendanceRules.PayCheck(meret, 0, 7, 0, false, 0, 9999, 4999), Is.EqualTo(AttendanceRules.PayStep.NoMeret));
        Assert.That(AttendanceRules.PayCheck(meret, 0, 7, 0, true, 0, 9999, 9999), Is.EqualTo(AttendanceRules.PayStep.NoCoupon));
    }

    // The event as the client reads it (gameeventcontext.cpp:424-445): the pay type byte, then with a
    // pay type the limit, the price and the coupon item id, then the gifts.
    [TestCase(false)]
    [TestCase(true)]
    public void EventBytes(bool withPay) {
        AttendGift.PayAttend? pay = withPay ? new AttendGift.PayAttend(AttendGiftCurrencyType.Meso, 5000, 3, 20000001) : null;
        var data = new AttendGift(new[] { new RewardItem(20000002, 1, 5) }, "n", "t", "c", "l", 60, null, pay);
        var gameEvent = new GameEvent(new GameEventMetadata(77, GameEventType.AttendGift, DateTime.UnixEpoch, DateTime.UnixEpoch.AddDays(1),
            TimeSpan.Zero, TimeSpan.Zero, Array.Empty<DayOfWeek>(), data, "", "", "", ""));
        var writer = new ByteWriter();
        gameEvent.WriteTo(writer);
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("AttendGift"));
        Assert.That(reader.ReadInt(), Is.EqualTo(77));
        reader.ReadLong();
        reader.ReadLong();
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("n"));
        Assert.That(reader.ReadString(), Is.EqualTo("l"));
        reader.ReadByte();
        reader.ReadByte();
        Assert.That(reader.ReadInt(), Is.EqualTo(60));
        reader.ReadByte();
        reader.ReadInt();
        Assert.That(reader.ReadByte(), Is.EqualTo(withPay ? 1 : 0));
        if (withPay) {
            Assert.That(reader.ReadInt(), Is.EqualTo(3));
            Assert.That(reader.ReadLong(), Is.EqualTo(5000));
            Assert.That(reader.ReadInt(), Is.EqualTo(20000001));
        }
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo(20000002));
        reader.ReadShort();
        reader.ReadInt();
        reader.ReadInt(); // four flag bytes
        Assert.That(reader.Available, Is.EqualTo(0));
    }
}
