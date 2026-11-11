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
}
