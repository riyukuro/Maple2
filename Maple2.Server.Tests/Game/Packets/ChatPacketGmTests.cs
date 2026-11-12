using System;
using System.Text;
using Maple2.Model.Enum;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// UserChat 0x1D: account, character, name, bool string id, text, i32 type, u8 GM flag, i32, [per type], u8.
public class ChatPacketGmTests {
    private const string Name = "gm";
    private const string Text = "hi";

    // Offset of the GM flag: opcode, two ids, two strings (u16 length + UTF-16), bool, i32 type.
    private static int FlagOffset => 2 + 8 + 8 + (2 + Name.Length * 2) + 1 + (2 + Text.Length * 2) + 4;

    private static byte Flag(ByteWriter writer) {
        return writer.Buffer[FlagOffset];
    }

    [TestCase(ChatType.Normal)]
    [TestCase(ChatType.Party)]
    [TestCase(ChatType.Guild)]
    [TestCase(ChatType.World)]
    [TestCase(ChatType.Channel)]
    [TestCase(ChatType.Super)]
    [TestCase(ChatType.Club)]
    public void Message_CarriesTheGmFlag(ChatType type) {
        ByteWriter gm = ChatPacket.Message(1, 2, Name, type, Text, gm: true);
        ByteWriter plain = ChatPacket.Message(1, 2, Name, type, Text);

        Assert.That(BitConverter.ToInt32(gm.Buffer, FlagOffset - 4), Is.EqualTo((int) type));
        Assert.That(Flag(gm), Is.EqualTo(1));
        Assert.That(Flag(plain), Is.EqualTo(0));
        Assert.That(gm.Length, Is.EqualTo(plain.Length));
    }

    [Test]
    public void Whisper_FlagOnlyOnTheReceivedLine() {
        ByteWriter received = ChatPacket.Whisper(1, 2, Name, Text, Name, gm: true);
        ByteWriter ownCopy = ChatPacket.Whisper(1, 2, Name, Text, gm: true);
        ByteWriter plain = ChatPacket.Whisper(1, 2, Name, Text, Name);

        Assert.That(Flag(received), Is.EqualTo(1));
        Assert.That(Flag(ownCopy), Is.EqualTo(0));
        Assert.That(Flag(plain), Is.EqualTo(0));
    }

    // The int after the GM flag is the sender's channel; the type's own field follows it.
    [TestCase(ChatType.World)]
    [TestCase(ChatType.Channel)]
    [TestCase(ChatType.Super)]
    public void Message_CarriesTheSendersChannel(ChatType type) {
        ByteWriter writer = ChatPacket.Message(1, 2, Name, type, Text, superChatId: 20300041, channel: 7);
        ByteWriter plain = ChatPacket.Message(1, 2, Name, type, Text, superChatId: 20300041);

        Assert.That(BitConverter.ToInt32(writer.Buffer, FlagOffset + 1), Is.EqualTo(7));
        Assert.That(BitConverter.ToInt32(plain.Buffer, FlagOffset + 1), Is.EqualTo(0));
        Assert.That(writer.Length, Is.EqualTo(plain.Length));
        if (type == ChatType.Super) {
            Assert.That(BitConverter.ToInt32(writer.Buffer, FlagOffset + 5), Is.EqualTo(20300041));
        }
    }

    // The received whisper carries the sender's profile picture after the int; the own copy has no string.
    [Test]
    public void Whisper_ReceivedLineCarriesThePicture() {
        const string picture = "profile/1/2.png";
        ByteWriter received = ChatPacket.Whisper(1, 2, Name, Text, picture);
        ByteWriter ownCopy = ChatPacket.Whisper(0, 2, Name, Text);

        int at = FlagOffset + 5;
        Assert.That(BitConverter.ToInt32(received.Buffer, FlagOffset - 4), Is.EqualTo((int) ChatType.WhisperFrom));
        Assert.That(BitConverter.ToInt16(received.Buffer, at), Is.EqualTo(picture.Length));
        Assert.That(Encoding.Unicode.GetString(received.Buffer, at + 2, picture.Length * 2), Is.EqualTo(picture));
        Assert.That(received.Length, Is.EqualTo(at + 2 + picture.Length * 2 + 1));
        Assert.That(BitConverter.ToInt32(ownCopy.Buffer, FlagOffset - 4), Is.EqualTo((int) ChatType.WhisperTo));
        Assert.That(ownCopy.Length, Is.EqualTo(at + 1));
    }

    [Test]
    public void Text_IsWhereTheOffsetAssumes() {
        ByteWriter writer = ChatPacket.Message(1, 2, Name, ChatType.Normal, Text);
        int textOffset = 2 + 8 + 8 + (2 + Name.Length * 2) + 1 + 2;

        Assert.That(Encoding.Unicode.GetString(writer.Buffer, textOffset, Text.Length * 2), Is.EqualTo(Text));
    }
}
