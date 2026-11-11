using System.Collections.Generic;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Tests.Validators;

public class InteractRulesTests {
    [Test]
    public void NamesBytes() {
        var writer = new ByteWriter();
        writer.WriteInt(2);
        writer.WriteString("a1");
        writer.WriteString("b2");
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.That(InteractRules.ReadNames(reader), Is.EqualTo(new[] { "a1", "b2" }));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void EndNeedsStart() {
        var started = new HashSet<string> { "a1" };
        Assert.That(InteractRules.CanEnd(started, "a1"), Is.True);
        Assert.That(InteractRules.CanEnd(started, "b2"), Is.False);
        Assert.That(InteractRules.CanEnd(new HashSet<string>(), "a1"), Is.False);
    }
}
