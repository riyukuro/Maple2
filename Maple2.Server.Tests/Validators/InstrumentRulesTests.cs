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

    // The client unlocks its score writer only for the results 8, 10 and 0x10 (UIWriteMusicDialog::OnMakeScoreResult).
    [Test]
    public void ComposeResultsAreTheOnesTheClientUnlocksOn() {
        Assert.That((byte) ComposeResult.Retry, Is.EqualTo(8));
        Assert.That((byte) ComposeResult.Close, Is.EqualTo(10));
        Assert.That((byte) ComposeResult.BannedWord, Is.EqualTo(0x10));
    }

    [TestCase(true, false, "Song", "cdefg", 2000, null)]
    [TestCase(false, false, "Song", "cdefg", 2000, ComposeResult.Close)] // no blank score with uses left
    [TestCase(true, true, "Song", "cdefg", 2000, ComposeResult.Close)] // already composed
    [TestCase(true, false, "a bad song", "cdefg", 2000, ComposeResult.BannedWord)]
    [TestCase(true, false, "Song", "cdefg", 1999, ComposeResult.Retry)]
    [TestCase(true, false, "", "cdefg", 2000, ComposeResult.Retry)]
    [TestCase(true, false, "Song", "", 2000, ComposeResult.Retry)]
    public void ComposeRefusal(bool usable, bool composed, string title, string mml, int length, ComposeResult? expected) {
        Assert.That(InstrumentRules.ComposeRefusal(usable, composed, title, Banned, mml, 5, length), Is.EqualTo(expected));
    }

    // A played score counts from the start to the stop, and no longer than the score.
    [TestCase(-5, 60000, 0)]
    [TestCase(0, 60000, 0)]
    [TestCase(30000, 60000, 30000)]
    [TestCase(90000, 60000, 60000)]
    [TestCase(90000, 0, 90000)] // a score without a length
    public void PlayedMs_StopsAtTheScoreLength(long elapsed, int length, long expected) {
        Assert.That(InstrumentRules.PlayedMs(elapsed, length), Is.EqualTo(expected));
    }

    // masteryValue per second, up to masteryValueMax.
    [TestCase(0, 1, 60, 0)]
    [TestCase(999, 1, 60, 0)]
    [TestCase(30000, 1, 60, 30)]
    [TestCase(90000, 1, 60, 60)]
    [TestCase(10000, 3, 100, 30)]
    [TestCase(10000, 1, -1, 0)]
    public void Mastery_IsPerSecondUpToTheMax(long played, int value, int max, int expected) {
        Assert.That(InstrumentRules.Mastery(played, value, max), Is.EqualTo(expected));
    }

    [TestCase(0, 0f)]
    [TestCase(300, 0f)]
    [TestCase(500, 0f)]
    [TestCase(1500, 2f)]
    [TestCase(60500, 120f)]
    public void ExpTicks_AreNeverNegative(long played, float expected) {
        Assert.That(InstrumentRules.ExpTicks(played), Is.EqualTo(expected));
    }

    [TestCase(0L, 1000L, true)]       // no expiry
    [TestCase(2000L, 1999L, true)]    // still in time
    [TestCase(2000L, 2000L, false)]   // the expiry second itself, as Item.IsExpired
    [TestCase(2000L, 5000L, false)]
    public void IsPlayable_RefusesAnExpiredInstrument(long expiryTime, long now, bool expected) {
        Assert.That(InstrumentRules.IsPlayable(expiryTime, now), Is.EqualTo(expected));
    }
}
