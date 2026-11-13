using Maple2.Model.Error;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Tests.Validators;

public class BeautyRulesTests {
    [Test]
    public void CreateBytes() {
        var writer = new ByteWriter();
        writer.WriteByte(2);
        writer.WriteByte(1);
        writer.WriteInt(10200001);
        writer.WriteLong(77);
        writer.WriteInt(0x1234); // first field of the appearance data
        var reader = new ByteReader(writer.ToArray(), 0);
        (byte shop, bool useVoucher, int itemId, long shopValue) = BeautyRules.ReadCreate(reader);
        Assert.That(shop, Is.EqualTo(2));
        Assert.That(useVoucher, Is.True);
        Assert.That(itemId, Is.EqualTo(10200001));
        Assert.That(shopValue, Is.EqualTo(77));
        Assert.That(reader.ReadInt(), Is.EqualTo(0x1234));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void DeleteBytes() {
        var writer = new ByteWriter();
        writer.WriteLong(5000);
        writer.WriteByte(0);
        var reader = new ByteReader(writer.ToArray(), 0);
        (long uid, byte slot) = BeautyRules.ReadDelete(reader);
        Assert.That(uid, Is.EqualTo(5000));
        Assert.That(slot, Is.EqualTo(0));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void DyeItem() {
        Assert.That(BeautyRules.DyeItemMatches(11300001, 11300001), Is.True);
        Assert.That(BeautyRules.DyeItemMatches(11300001, 11300002), Is.False);
    }

    // Mode 22: a refused entry is read to its end, hat or not, and the next entry is read whole.
    [TestCase(true, 72)]
    [TestCase(false, 20)]
    public void DyeEntries(bool cap, int colorBytes) {
        var writer = new ByteWriter();
        for (int i = 0; i < 2; i++) {
            writer.WriteBool(true);
            writer.WriteByte(1);
            writer.WriteBool(i == 1);
            writer.WriteByte();
            writer.WriteInt();
            writer.WriteLong();
            writer.WriteLong(7000 + i);
            writer.WriteInt(11300001 + i);
            writer.WriteBytes(new byte[i == 0 ? colorBytes : 20]);
        }
        writer.WriteBool(false);

        var reader = new ByteReader(writer.ToArray(), 0);
        (bool UseVoucher, long Uid, int ItemId)? first = BeautyRules.ReadDyeHead(reader);
        Assert.That(first, Is.EqualTo((false, 7000L, 11300001)));
        BeautyRules.ReadDyeColors(reader, cap);
        (bool UseVoucher, long Uid, int ItemId)? second = BeautyRules.ReadDyeHead(reader);
        Assert.That(second, Is.EqualTo((true, 7001L, 11300002)));
        BeautyRules.ReadDyeColors(reader, false);
        Assert.That(BeautyRules.ReadDyeHead(reader), Is.Null);
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void Fee() {
        Assert.That(BeautyRules.Affords(100, 100), Is.True);
        Assert.That(BeautyRules.Affords(99, 100), Is.False);
        Assert.That(BeautyRules.Affords(0, 0), Is.True);
        Assert.That(BeautyRules.Affords(100, -1), Is.False);
        Assert.That(BeautyRules.Affords(-1, 0), Is.False);
    }

    // Modes 17 and 19.
    [TestCase(0, 3, 9, 3)]
    [TestCase(6, 3, 9, 9)]
    [TestCase(9, 3, 9, -1)]
    [TestCase(7, 3, 9, -1)]
    [TestCase(0, 0, 9, -1)]
    [TestCase(0, 3, 0, -1)]
    [TestCase(-3, 3, 9, -1)]
    public void Extend(int extended, int perExtension, int maxExtra, int expected) {
        Assert.That(BeautyRules.ExtendedSlots(extended, perExtension, maxExtra), Is.EqualTo(expected));
    }

    // The client's constants: 30 slots, 1 per extension, 5 extensions at most.
    [TestCase(30, 0, 5, 30)]
    [TestCase(30, 5, 5, 35)]
    [TestCase(30, 11, 5, 35)]
    [TestCase(0, 2, 5, 38)]
    public void StyleSlots(int baseSlots, int extended, int maxExtra, int expected) {
        Assert.That(BeautyRules.StyleSlots(baseSlots, extended, maxExtra), Is.EqualTo(expected));
    }

    // 30 saved styles in 30 slots: nothing is free, so the save has to pay for an extension first.
    [Test]
    public void FullAtClientBase() {
        Assert.That(BeautyRules.OpenStyleSlots(30, 0, 5, 29), Is.EqualTo(1));
        Assert.That(BeautyRules.OpenStyleSlots(30, 0, 5, 30), Is.EqualTo(0));
        Assert.That(BeautyRules.OpenStyleSlots(30, 0, 5, 33), Is.EqualTo(0));
    }

    // Five paid extensions end at the client's 35 slots; a sixth is refused.
    [Test]
    public void FiveExtensions() {
        int extended = 0;
        for (int i = 0; i < 5; i++) {
            extended = BeautyRules.ExtendedSlots(extended, 1, 5);
            Assert.That(BeautyRules.OpenStyleSlots(30, extended, 5, 30 + i), Is.EqualTo(1));
        }
        Assert.That(BeautyRules.StyleSlots(30, extended, 5), Is.EqualTo(35));
        Assert.That(BeautyRules.ExtendedSlots(extended, 1, 5), Is.EqualTo(-1));
    }

    // Mode 17 from the first full list to the last extension: each save buys one extension, the save
    // after the last is refused with the client's code 19, a save without merets with code 20, and a
    // list with an open slot buys nothing.
    [Test]
    public void FullExtendSave() {
        int extended = 0;
        long meret = 5 * 490;
        for (int saved = 30; saved < 35; saved++) {
            Assert.That(BeautyRules.SaveNeedsExtension(BeautyRules.OpenStyleSlots(30, extended, 5, saved)), Is.True);
            Assert.That(BeautyRules.ExtendCheck(extended, 1, 5, meret, 490), Is.EqualTo(BeautyRules.ExtendStep.Extend));
            extended = BeautyRules.ExtendedSlots(extended, 1, 5);
            meret -= 490;
            Assert.That(BeautyRules.OpenStyleSlots(30, extended, 5, saved), Is.EqualTo(1));
        }
        Assert.That(BeautyRules.ExtendCheck(extended, 1, 5, 490, 490), Is.EqualTo(BeautyRules.ExtendStep.AtMax));
        Assert.That(BeautyRules.ExtendCheck(0, 1, 5, 489, 490), Is.EqualTo(BeautyRules.ExtendStep.NoMeret));
        Assert.That(BeautyRules.SaveNeedsExtension(1), Is.False);
    }

    // The mode 8 codes of the client's switches.
    [Test]
    public void ErrorCodes() {
        Assert.That((int) BeautyError.lack_currency, Is.EqualTo(2));
        Assert.That((int) BeautyError.s_beauty_msg_error_color, Is.EqualTo(0xc));
        Assert.That((int) BeautyError.none, Is.EqualTo(0x12));
        Assert.That((int) BeautyError.s_beauty_msg_error_style_slot_max, Is.EqualTo(0x13));
        Assert.That((int) BeautyError.s_beauty_msg_error_style_slot_extend_max, Is.EqualTo(0x13));
        Assert.That((int) BeautyError.s_err_lack_merat_ask, Is.EqualTo(0x14));
        Assert.That((int) BeautyError.s_beauty_msg_error_style_disable_gender, Is.EqualTo(0x17));
        Assert.That((int) BeautyError.s_beauty_msg_error_style_apply_already, Is.EqualTo(0x18));
        Assert.That((int) BeautyError.s_beauty_msg_error_style_save_cant_not_beauty_day, Is.EqualTo(0x19));
    }

    // Mode 17: i64 item uid. Mode 19: nothing.
    [Test]
    public void ExtendBytes() {
        var writer = new ByteWriter();
        writer.WriteLong(0x1122334455667788);
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.That(reader.ReadLong(), Is.EqualTo(0x1122334455667788));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    // The trophy part of a shop row locks until the row's grade is cleared; a grade 0 row is open, as the client's
    // start grade (1 or more) is always past it (uibeautyshopstyledialog.cpp:702, achievemanager.cpp:452).
    [TestCase(0, 0, false, false)]
    [TestCase(21100001, 0, false, false)]
    [TestCase(21100001, 2, false, true)]
    [TestCase(21100001, 2, true, false)]
    public void IsLocked_TrophyGrade(int achievementId, int rank, bool hasGrade, bool locked) {
        Assert.That(BeautyRules.IsLocked(60, 0, achievementId, rank, () => hasGrade), Is.EqualTo(locked));
    }

    [Test]
    public void IsLocked_Level() {
        Assert.That(BeautyRules.IsLocked(39, 40, 0, 0, () => true), Is.True);
        Assert.That(BeautyRules.IsLocked(40, 40, 0, 0, () => true), Is.False);
    }
}
