using System;
using System.Collections.Generic;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Tests.Validators;

public class AttendanceRulesTests {
    // Claim and bought day: a gift that is not given takes nothing; a given one takes once, after it.
    [Test]
    public void GiveThenTake() {
        var order = new List<string>();
        Assert.That(AttendanceRules.GiveThenTake(() => { order.Add("give"); return false; }, () => order.Add("take")), Is.False);
        Assert.That(order, Is.EqualTo(new[] { "give" }));

        order.Clear();
        long meso = 500;
        int paid = 0;
        Assert.That(AttendanceRules.GiveThenTake(() => { order.Add("give"); return true; }, () => { order.Add("take"); meso -= 200; paid++; }), Is.True);
        Assert.That(order, Is.EqualTo(new[] { "give", "take" }));
        Assert.That((meso, paid), Is.EqualTo((300L, 1)));
    }

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

    // A price below 0 (a PayAttend not made by ParsePay) is refused: the handler takes a price that is not above 0
    // with ChangeMeso / ChangeMeret(-price), so it was added.
    [TestCase(AttendGiftCurrencyType.Meso)]
    [TestCase(AttendGiftCurrencyType.Meret)]
    public void PayCheck_ANegativePrice_IsRefused(AttendGiftCurrencyType type) {
        var pay = new AttendGift.PayAttend(type, -100, 3, 0);

        Assert.That(AttendanceRules.PayCheck(pay, 0, 7, 0, false, 0, 9999, 9999), Is.EqualTo(AttendanceRules.PayStep.Refuse));
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
    // The requirement kind and value after the play seconds (gameeventcontext.cpp:434-435), in the client's
    // numbering, which differs from the server enum's order.
    [TestCase(null, 0, 0, (byte) 0, 0)]
    [TestCase(AttendGiftRequirement.None, 5, 0, (byte) 0, 0)]
    [TestCase(AttendGiftRequirement.UserValue, 3000, 0, (byte) 1, 3000)]
    [TestCase(AttendGiftRequirement.ItemId, 20300001, 7, (byte) 2, 20300001)]
    [TestCase(AttendGiftRequirement.NotUserValue, 3001, 0, (byte) 3, 3001)]
    public void RequirementBytes(AttendGiftRequirement? type, int value1, int value2, byte kind, int value) {
        AttendGift.Require? require = type == null ? null : new AttendGift.Require(type.Value, value1, value2);
        var data = new AttendGift(new[] { new RewardItem(20000002, 1, 5) }, "n", "t", "c", "l", 60, require, null);
        var gameEvent = new GameEvent(new GameEventMetadata(77, GameEventType.AttendGift, DateTime.UnixEpoch, DateTime.UnixEpoch.AddDays(1),
            TimeSpan.Zero, TimeSpan.Zero, Array.Empty<DayOfWeek>(), data, "", "", "", ""));
        var writer = new ByteWriter();
        gameEvent.WriteTo(writer);
        var reader = new ByteReader(writer.ToArray(), 0);
        reader.ReadUnicodeString();
        reader.ReadInt();
        reader.ReadLong();
        reader.ReadLong();
        reader.ReadUnicodeString();
        reader.ReadString();
        reader.ReadByte();
        reader.ReadByte();
        Assert.That(reader.ReadInt(), Is.EqualTo(60));
        Assert.That(reader.ReadByte(), Is.EqualTo(kind));
        Assert.That(reader.ReadInt(), Is.EqualTo(value));
        Assert.That(reader.ReadByte(), Is.EqualTo(0)); // no pay type
    }

    // ParseBoolW: true when the text holds any of 1, o, O, t, T.
    [TestCase(null, false)]
    [TestCase("", false)]
    [TestCase("True", true)]
    [TestCase("1", true)]
    [TestCase("on", true)]
    [TestCase("False", false)]
    [TestCase("0", false)]
    public void UserValueTrue(string? text, bool result) {
        Assert.That(AttendanceRules.UserValueTrue(text), Is.EqualTo(result));
    }

    // Kind 1 needs a value that reads true, kind 3 no value, kind 2 the item start, no requirement always is.
    [Test]
    public void RequirementMet() {
        var values = new Dictionary<int, string> { { 3000, "True" }, { 3002, "False" } };
        string? Lookup(int type) => values.TryGetValue(type, out string? text) ? text : null;

        Assert.That(AttendanceRules.RequirementMet(null, Lookup, false), Is.True);
        Assert.That(AttendanceRules.RequirementMet(new AttendGift.Require(AttendGiftRequirement.None, 0, 0), Lookup, false), Is.True);
        Assert.That(AttendanceRules.RequirementMet(new AttendGift.Require(AttendGiftRequirement.UserValue, 3000, 0), Lookup, false), Is.True);
        Assert.That(AttendanceRules.RequirementMet(new AttendGift.Require(AttendGiftRequirement.UserValue, 3002, 0), Lookup, false), Is.False);
        Assert.That(AttendanceRules.RequirementMet(new AttendGift.Require(AttendGiftRequirement.UserValue, 3001, 0), Lookup, false), Is.False);
        Assert.That(AttendanceRules.RequirementMet(new AttendGift.Require(AttendGiftRequirement.NotUserValue, 3001, 0), Lookup, false), Is.True);
        Assert.That(AttendanceRules.RequirementMet(new AttendGift.Require(AttendGiftRequirement.NotUserValue, 3002, 0), Lookup, false), Is.False);
        Assert.That(AttendanceRules.RequirementMet(new AttendGift.Require(AttendGiftRequirement.ItemId, 20300001, 7), Lookup, false), Is.False);
        Assert.That(AttendanceRules.RequirementMet(new AttendGift.Require(AttendGiftRequirement.ItemId, 20300001, 7), Lookup, true), Is.True);
        Assert.That(AttendanceRules.RequirementMet(new AttendGift.Require(AttendGiftRequirement.UserValue, 3001, 0), Lookup, true), Is.False);
    }

    private static AttendGift ItemGift(AttendGiftRequirement type, int value1) {
        return new AttendGift([], "gift", "title", "content", string.Empty, 0, new AttendGift.Require(type, value1, 7));
    }

    // The event whose kind 2 requirement names the item; a user value requirement with the same number is not it.
    [Test]
    public void StartByItem() {
        var events = new List<(int, AttendGift)> {
            (1, ItemGift(AttendGiftRequirement.UserValue, 20300636)),
            (2, ItemGift(AttendGiftRequirement.ItemId, 20301532)),
            (3, ItemGift(AttendGiftRequirement.ItemId, 20300636)),
        };
        Assert.That(AttendanceRules.StartByItem(events, 20300636, _ => false), Is.EqualTo((AttendanceRules.ItemStart.Start, 3)));
        Assert.That(AttendanceRules.StartByItem(events, 20301532, id => id == 2), Is.EqualTo((AttendanceRules.ItemStart.AlreadyStarted, 2)));
        Assert.That(AttendanceRules.StartByItem(events, 30000717, _ => false), Is.EqualTo((AttendanceRules.ItemStart.NotFound, 0)));
        // Two events name the same item: the first not yet started is the one started.
        events.Add((4, ItemGift(AttendGiftRequirement.ItemId, 20301532)));
        Assert.That(AttendanceRules.StartByItem(events, 20301532, id => id == 2), Is.EqualTo((AttendanceRules.ItemStart.Start, 4)));
        Assert.That(AttendanceRules.StartByItem(events, 20301532, _ => true), Is.EqualTo((AttendanceRules.ItemStart.AlreadyStarted, 2)));
    }

    [TestCase(1000L, 7, 10000000L, 1000L + 7 * 86400L)]
    [TestCase(1000L, 92, 2000000L, 2000000L)]
    [TestCase(1000L, 0, 2000000L, 2000000L)]
    public void ItemStartEnd(long now, int days, long eventEnd, long result) {
        Assert.That(AttendanceRules.ItemStartEnd(now, days, eventEnd), Is.EqualTo(result));
    }

    [Test]
    public void TabNameIsTheTabButton() {
        // Every KR and NA AttendGift row names its tab with tabButton.
        Assert.That(AttendanceRules.TabName("Gift", null), Is.EqualTo("Gift"));
        Assert.That(AttendanceRules.TabName("Tab", "Old"), Is.EqualTo("Tab"));
        Assert.That(AttendanceRules.TabName(null, "Old"), Is.EqualTo("Old"));
        Assert.That(AttendanceRules.TabName(null, null), Is.EqualTo(string.Empty));
    }

    [Test]
    public void OnlyEventsWithoutPlayTimeAttendAtGameStart() {
        Assert.That(AttendanceRules.AttendsAtGameStart(0), Is.True);
        Assert.That(AttendanceRules.AttendsAtGameStart(1), Is.False);
        Assert.That(AttendanceRules.AttendsAtGameStart(1800), Is.False);
    }

    [Test]
    public void AttendedToday() {
        var now = new DateTime(2026, 11, 12, 10, 0, 0);
        Assert.That(AttendanceRules.AttendedToday(new DateTime(2026, 11, 12, 0, 5, 0), now), Is.True);
        Assert.That(AttendanceRules.AttendedToday(new DateTime(2026, 11, 13, 0, 0, 0), now), Is.True);
        Assert.That(AttendanceRules.AttendedToday(new DateTime(2026, 11, 11, 23, 59, 0), now), Is.False);
        Assert.That(AttendanceRules.AttendedToday(DateTime.UnixEpoch, now), Is.False);
    }
}
