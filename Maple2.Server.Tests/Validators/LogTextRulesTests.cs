using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Tests.Validators;

public class LogTextRulesTests {
    // Mode 0: u8 0, u8 0, two strings.
    [Test]
    public void LogBytes() {
        var writer = new ByteWriter();
        writer.WriteByte(0);
        writer.WriteByte(0);
        writer.WriteUnicodeString("key\r\nforged line");
        writer.WriteUnicodeString(new string('v', 2000));
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(LogTextRules.Clean(reader.ReadUnicodeString()), Is.EqualTo("key  forged line"));
        Assert.That(LogTextRules.Clean(reader.ReadUnicodeString()).Length, Is.EqualTo(LogTextRules.MaxLength));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void Clean() {
        Assert.That(LogTextRules.Clean(null), Is.EqualTo(string.Empty));
        Assert.That(LogTextRules.Clean("plain text 123"), Is.EqualTo("plain text 123"));
        Assert.That(LogTextRules.Clean("a\tb\0c" + (char) 0x1b + "d" + (char) 0x2028 + "e"), Is.EqualTo("a b c d e"));
        Assert.That(LogTextRules.Clean(new string('a', LogTextRules.MaxLength)).Length, Is.EqualTo(LogTextRules.MaxLength));
    }
}
