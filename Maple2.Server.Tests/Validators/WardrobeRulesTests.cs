using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Tests.Validators;

public class WardrobeRulesTests {
    [Test]
    public void RenameBytes() {
        var writer = new ByteWriter();
        writer.WriteInt(2);
        writer.WriteUnicodeString("Raid");
        var reader = new ByteReader(writer.ToArray(), 0);
        (int index, string name) = WardrobeRules.ReadRename(reader);
        Assert.That(index, Is.EqualTo(2));
        Assert.That(name, Is.EqualTo("Raid"));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void Name() {
        Assert.That(WardrobeRules.NameAllowed("Raid", 10), Is.True);
        Assert.That(WardrobeRules.NameAllowed(new string('a', 10), 10), Is.True);
        Assert.That(WardrobeRules.NameAllowed(new string('a', 11), 10), Is.False);
    }

    [Test]
    public void BannedName() {
        Assert.That(WardrobeRules.NameBanned("Raid", name => name.Contains("ai")), Is.True);
        Assert.That(WardrobeRules.NameBanned("Raid", name => name.Contains("xx")), Is.False);
        Assert.That(WardrobeRules.NameBanned("", _ => true), Is.False);
    }

    // Only a refused name on an existing tab is answered with the tab; every other path sends no tab.
    [TestCase(false, false, true, WardrobeRules.RenameStep.Ignore)]
    [TestCase(false, true, true, WardrobeRules.RenameStep.Ignore)]
    [TestCase(true, true, true, WardrobeRules.RenameStep.RefuseRestore)]
    [TestCase(true, true, false, WardrobeRules.RenameStep.Refuse)]
    [TestCase(true, false, true, WardrobeRules.RenameStep.Rename)]
    [TestCase(true, false, false, WardrobeRules.RenameStep.Create)]
    public void RenameSteps(bool allowed, bool banned, bool exists, WardrobeRules.RenameStep expected) {
        Assert.That(WardrobeRules.Rename(allowed, banned, exists), Is.EqualTo(expected));
    }
}
