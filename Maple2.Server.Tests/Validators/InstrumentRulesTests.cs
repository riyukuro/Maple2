using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Tests.Validators;

public class InstrumentRulesTests {
    private static bool Banned(string text) => text.Contains("bad");

    // PlayInstrument 0x0074 mode 8 after the mode byte: i64 score uid, i32 length (ms), i32 instrument,
    // title, mml.
    private static bool Compose(string title, string mml, int length, int noteLengthMax) {
        var writer = new ByteWriter();
        writer.WriteLong(77);
        writer.WriteInt(length);
        writer.WriteInt(1);
        writer.WriteUnicodeString(title);
        writer.WriteString(mml);

        var packet = new ByteReader(writer.ToArray(), 0);
        Assert.That(packet.ReadLong(), Is.EqualTo(77));
        int readLength = packet.ReadInt();
        Assert.That(packet.ReadInt(), Is.EqualTo(1));
        string readTitle = packet.ReadUnicodeString();
        string readMml = packet.ReadString();
        Assert.That(packet.Available, Is.EqualTo(0));
        return InstrumentRules.IsComposable(readTitle, Banned, readMml, noteLengthMax, readLength);
    }

    [TestCase("Song", "cdefg", 2000, 5, true)]
    [TestCase("Song", "cdefg", 1999, 5, false)] // under two seconds
    [TestCase("Song", "cdefgh", 2000, 5, true)] // the client allows the item's limit plus one per tab
    [TestCase("Song", "cdefghi", 2000, 5, false)]
    [TestCase("Song", "cde:fg:a", 2000, 5, true)] // three tabs: 5 + 3
    [TestCase("Song", "cde:fg:ab", 2000, 5, false)]
    [TestCase("Song", "", 2000, 5, false)]
    [TestCase("", "cdefg", 2000, 5, false)]
    [TestCase("   ", "cdefg", 2000, 5, false)]
    [TestCase("a bad song", "cdefg", 2000, 5, false)]
    public void ComposeChecks(string title, string mml, int length, int noteLengthMax, bool expected) {
        Assert.That(Compose(title, mml, length, noteLengthMax), Is.EqualTo(expected));
    }
}
