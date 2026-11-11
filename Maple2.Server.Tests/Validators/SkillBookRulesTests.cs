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

    [TestCase(0, 3, true)]
    [TestCase(3, 3, true)]
    [TestCase(4, 3, false)]
    [TestCase(-1, 3, false)]
    [TestCase(int.MaxValue, 3, false)]
    public void TabCount(int count, int max, bool expected) {
        Assert.That(SkillBookRules.TabCountAllowed(count, max), Is.EqualTo(expected));
    }
}
