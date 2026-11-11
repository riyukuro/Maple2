using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Tests.Validators;

public class PartySearchRulesTests {
    [Test]
    public void AddBytes() {
        var writer = new ByteWriter();
        writer.WriteUnicodeString("raid tonight");
        writer.WriteBool(true);
        writer.WriteInt(4);
        var reader = new ByteReader(writer.ToArray(), 0);
        (string title, bool noApproval, int size) = PartySearchRules.ReadAdd(reader);
        Assert.That(title, Is.EqualTo("raid tonight"));
        Assert.That(noApproval, Is.True);
        Assert.That(size, Is.EqualTo(4));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [TestCase("ab", 4, true)]
    [TestCase("ab", 10, true)]
    [TestCase("a", 4, false)]
    [TestCase("", 4, false)]
    [TestCase("ab", 0, false)]
    [TestCase("ab", -1, false)]
    [TestCase("ab", 5, false)]
    [TestCase("ab", int.MaxValue, false)]
    public void ValidAdd(string title, int size, bool expected) {
        Assert.That(PartySearchRules.ValidAdd(title, size, new[] { 4, 6, 10 }), Is.EqualTo(expected));
    }

    [Test]
    public void NoList() {
        Assert.That(PartySearchRules.ValidAdd("ab", 10, null), Is.True);
        Assert.That(PartySearchRules.ValidAdd("ab", 10, new int[0]), Is.True);
        Assert.That(PartySearchRules.ValidAdd("ab", 4, null), Is.False);
    }
}
