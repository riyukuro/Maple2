using System;
using Maple2.Model.Game;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Tests.Core.Packets;

// Notice mode 4 as the client reads it: mode byte, flags short, the text, then a short repeat count when the flags hold
// Mint (0x10) and an int when they hold LargeAlert (0x200) (the notice decoder and CSceneStateGame::OnNotice).
// Message with such flags (the notice GM command) must write them like Notice.
public class NoticeMessageFlagsTests {
    private static byte[] Bytes(NoticePacket.Flags flags) =>
        NoticePacket.Message(new InterfaceText("ab"), flags).ToArray();

    [Test]
    public void Mint_WritesTheRepeatCount() {
        byte[] plain = Bytes(NoticePacket.Flags.Message);
        byte[] mint = Bytes(NoticePacket.Flags.Mint);
        Assert.That(mint, Has.Length.EqualTo(plain.Length + 2));
        Assert.That(BitConverter.ToInt16(mint, 3), Is.EqualTo((short) NoticePacket.Flags.Mint));
        Assert.That(BitConverter.ToInt16(mint, mint.Length - 2), Is.EqualTo(0));
    }

    [Test]
    public void LargeAlert_WritesTheValue() {
        byte[] plain = Bytes(NoticePacket.Flags.Message);
        byte[] large = Bytes(NoticePacket.Flags.LargeAlert);
        Assert.That(large, Has.Length.EqualTo(plain.Length + 4));
        Assert.That(BitConverter.ToInt32(large, large.Length - 4), Is.EqualTo(0));
    }

    [Test]
    public void MessageAlert_IsUnchanged() {
        var flags = NoticePacket.Flags.Message | NoticePacket.Flags.Alert;
        Assert.That(Bytes(flags), Is.EqualTo(NoticePacket.Notice(flags, new InterfaceText("ab")).ToArray()));
        Assert.That(Bytes(flags), Has.Length.EqualTo(Bytes(NoticePacket.Flags.Message).Length));
    }
}
