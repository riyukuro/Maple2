using System.Collections.Generic;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Tests.Validators;

public class SkillMacroRulesTests {
    // SkillMacro 0x007B mode 1 starts with the i32 tab count.
    [TestCase(1, true)]
    [TestCase(3, true)]
    [TestCase(0, true)] // the last tab removed
    [TestCase(4, false)]
    [TestCase(-1, false)]
    [TestCase(int.MaxValue, false)]
    public void TabCount(int count, bool expected) {
        var writer = new ByteWriter();
        writer.WriteInt(count);
        var packet = new ByteReader(writer.ToArray(), 0);
        Assert.That(SkillMacroRules.IsTabCount(packet.ReadInt()), Is.EqualTo(expected));
    }

    // One tab: name, i64 key, i32 skill count, the ids. The count must fit in what is left of the packet.
    [TestCase(2, true)]
    [TestCase(0, true)]
    [TestCase(3, false)]
    [TestCase(-1, false)]
    [TestCase(0x40000000, false)]
    public void SkillCountFitsThePacket(int count, bool expected) {
        var writer = new ByteWriter();
        writer.WriteUnicodeString("A");
        writer.WriteLong(7);
        writer.WriteInt(count);
        writer.WriteInt(10500001);
        writer.WriteInt(10500002);
        var packet = new ByteReader(writer.ToArray(), 0);
        Assert.That(packet.ReadUnicodeString(), Is.EqualTo("A"));
        Assert.That(packet.ReadLong(), Is.EqualTo(7));
        Assert.That(SkillMacroRules.IsSkillCount(packet.ReadInt(), packet.Available), Is.EqualTo(expected));
    }

    [Test]
    public void Names() {
        var names = new List<string> { "A" };
        Assert.That(SkillMacroRules.IsNewName("B", names), Is.True);
        Assert.That(SkillMacroRules.IsNewName("A", names), Is.False);
        Assert.That(SkillMacroRules.IsNewName("a", names), Is.False); // the client compares without case
        Assert.That(SkillMacroRules.IsNewName("", names), Is.False);
        Assert.That(SkillMacroRules.IsNewName(new string('x', 5), names), Is.True);
        Assert.That(SkillMacroRules.IsNewName(new string('x', 6), names), Is.False);
    }

    // Mode 1's tab names as they come: the box's 5 characters, and the client's own default name on the first tab.
    [TestCase(16, 5, true)]
    [TestCase(17, 5, false)]
    [TestCase(5, 6, false)]
    [TestCase(5, 16, false)]
    public void NameLengthByTab(int first, int second, bool expected) {
        var writer = new ByteWriter();
        writer.WriteUnicodeString(new string('x', first));
        writer.WriteUnicodeString(new string('y', second));
        var packet = new ByteReader(writer.ToArray(), 0);
        var names = new List<string>();
        bool ok = true;
        for (int i = 0; i < 2; i++) {
            string name = packet.ReadUnicodeString();
            ok &= SkillMacroRules.IsNewName(name, names);
            names.Add(name);
        }
        Assert.That(ok, Is.EqualTo(expected));
    }

    [TestCase(0, 10500001, true)]
    [TestCase(10500001, 10500002, true)]
    [TestCase(10500002, 10500002, false)]
    [TestCase(10500002, 10500001, false)]
    [TestCase(0, 0, false)]
    [TestCase(0, -5, false)]
    public void SkillsRise(int previous, int skillId, bool expected) {
        Assert.That(SkillMacroRules.IsNextSkill(previous, skillId), Is.EqualTo(expected));
    }
}
