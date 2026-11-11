using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Tests.Validators;

public class SkillBookRulesTests {
    [Test]
    public void RenameBytes() {
        var writer = new ByteWriter();
        writer.WriteLong(0x1122334455667788);
        writer.WriteUnicodeString("Build");
        var reader = new ByteReader(writer.ToArray(), 0);
        (long tabId, string name) = SkillBookRules.ReadRename(reader);
        Assert.That(tabId, Is.EqualTo(0x1122334455667788));
        Assert.That(name, Is.EqualTo("Build"));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void Name() {
        Assert.That(SkillBookRules.NameAllowed("Build"), Is.True);
        Assert.That(SkillBookRules.NameAllowed(""), Is.False);
        Assert.That(SkillBookRules.NameAllowed("  "), Is.False);
        Assert.That(SkillBookRules.NameAllowed(new string('a', SkillBookRules.MaxNameLength)), Is.True);
        Assert.That(SkillBookRules.NameAllowed(new string('a', SkillBookRules.MaxNameLength + 1)), Is.False);
    }

    [Test]
    public void BannedName() {
        Assert.That(SkillBookRules.NameBanned("Build", name => name.Contains("ui")), Is.True);
        Assert.That(SkillBookRules.NameBanned("Build", name => name.Contains("xx")), Is.False);
        Assert.That(SkillBookRules.NameBanned("", _ => true), Is.False);
    }

    // Mode 4: i32 UI id of the skill window, bool coupon check box.
    [TestCase(true)]
    [TestCase(false)]
    public void ExpandBytes(bool coupon) {
        var writer = new ByteWriter();
        writer.WriteInt(0x4F2D);
        writer.WriteBool(coupon);
        var reader = new ByteReader(writer.ToArray(), 0);
        (int dialogId, bool useCoupon) = SkillBookRules.ReadExpand(reader);
        Assert.That(dialogId, Is.EqualTo(SkillBookRules.DialogId));
        Assert.That(useCoupon, Is.EqualTo(coupon));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [TestCase(true, true, 0, 1000, SkillBookRules.ExpandPayment.Coupon)]
    [TestCase(true, true, 5000, 1000, SkillBookRules.ExpandPayment.Coupon)]
    [TestCase(true, false, 1000, 1000, SkillBookRules.ExpandPayment.Meret)]
    [TestCase(true, false, 999, 1000, SkillBookRules.ExpandPayment.None)]
    [TestCase(false, true, 1000, 1000, SkillBookRules.ExpandPayment.Meret)]
    [TestCase(false, true, 0, 1000, SkillBookRules.ExpandPayment.None)]
    [TestCase(false, false, 5000, -1, SkillBookRules.ExpandPayment.None)]
    public void ExpandPaidBy(bool useCoupon, bool hasCoupon, long meret, long price, SkillBookRules.ExpandPayment expected) {
        Assert.That(SkillBookRules.ExpandPaidBy(useCoupon, hasCoupon, meret, price), Is.EqualTo(expected));
    }

    [TestCase(0, 3, true)]
    [TestCase(3, 3, true)]
    [TestCase(4, 3, false)]
    [TestCase(-1, 3, false)]
    [TestCase(int.MaxValue, 3, false)]
    public void TabCount(int count, int max, bool expected) {
        Assert.That(SkillBookRules.TabCountAllowed(count, max), Is.EqualTo(expected));
    }
}
