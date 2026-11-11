using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Tests.Validators;

public class HomeOpenRulesTests {
    // RequestCube 24 after the mode byte: kind, text, flag.
    private static bool Read(byte[] body, out string passcode) {
        var packet = new ByteReader(body, 0);
        byte kind = packet.ReadByte();
        string text = packet.ReadUnicodeString();
        return HomeOpenRules.TryGetPasscode(kind, text, out passcode);
    }

    private static byte[] Body(byte kind, string text, byte flag = 1) {
        var writer = new ByteWriter();
        writer.WriteByte(kind);
        writer.WriteUnicodeString(text);
        writer.WriteByte(flag);
        return writer.ToArray();
    }

    [Test]
    public void PublicClearsThePasscode() {
        // Kind 0, empty text (00 00), flag 1.
        Assert.That(Read(new byte[] { 0x00, 0x00, 0x00, 0x01 }, out string passcode), Is.True);
        Assert.That(passcode, Is.Empty);
        Assert.That(HomeOpenRules.IsPrivate(passcode), Is.False);
    }

    [Test]
    public void PasswordIsSixDigits() {
        // Kind 1, "123456" (06 00 and six wide digits), flag 0.
        byte[] body = { 0x01, 0x06, 0x00, 0x31, 0x00, 0x32, 0x00, 0x33, 0x00, 0x34, 0x00, 0x35, 0x00, 0x36, 0x00, 0x00 };
        Assert.That(Read(body, out string passcode), Is.True);
        Assert.That(passcode, Is.EqualTo("123456"));

        Assert.That(Read(Body(1, "12345"), out _), Is.False);
        Assert.That(Read(Body(1, "1234567"), out _), Is.False);
        Assert.That(Read(Body(1, "12345a"), out _), Is.False);
        Assert.That(Read(Body(1, "+12345"), out _), Is.False);
        Assert.That(Read(Body(1, ""), out _), Is.False);
    }

    [Test]
    public void PrivateIsNotPublic() {
        // Kind 2, empty text, flag 1: the home closes, the passcode is not cleared.
        Assert.That(Read(new byte[] { 0x02, 0x00, 0x00, 0x01 }, out string passcode), Is.True);
        Assert.That(passcode, Is.Not.Empty);
        Assert.That(HomeOpenRules.IsPrivate(passcode), Is.True);
        // No password a visitor can type equals it, and no client can set it as a password.
        Assert.That(HomeOpenRules.IsPasscode(passcode), Is.False);
        Assert.That(Read(Body(1, HomeOpenRules.PrivatePasscode), out _), Is.False);
    }

    [Test]
    public void OtherKindsAreRefused() {
        Assert.That(Read(Body(3, ""), out _), Is.False);
        Assert.That(Read(Body(0xFF, "123456"), out _), Is.False);
    }
}
