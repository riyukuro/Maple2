using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Tests.Validators;

public class ReportRulesTests {
    // The home report: name, text, u8 4, i32 flags, i64 home, i32 map, i32 plot.
    [Test]
    public void HeadBytes() {
        var writer = new ByteWriter();
        writer.WriteUnicodeString("Target");
        writer.WriteUnicodeString("reason");
        writer.WriteByte(4);
        writer.WriteInt(0x12);
        writer.WriteLong(5000);
        writer.WriteInt(62000000);
        writer.WriteInt(3);
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.That(ReportRules.ReadHead(reader), Is.EqualTo(("Target", "reason", (byte) 4, 0x12)));
        Assert.That(reader.ReadLong(), Is.EqualTo(5000));
        Assert.That(reader.ReadInt(), Is.EqualTo(62000000));
        Assert.That(reader.ReadInt(), Is.EqualTo(3));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void Lengths() {
        Assert.That(ReportRules.TextValid(""), Is.False);
        Assert.That(ReportRules.TextValid("a"), Is.True);
        Assert.That(ReportRules.TextValid(new string('a', 4000)), Is.True);
        Assert.That(ReportRules.TextValid(new string('a', 4001)), Is.False);
        Assert.That(ReportRules.AttachedValid(""), Is.True);
        Assert.That(ReportRules.AttachedValid(new string('a', 4000)), Is.True);
        Assert.That(ReportRules.AttachedValid(new string('a', 4001)), Is.False);
    }
}
