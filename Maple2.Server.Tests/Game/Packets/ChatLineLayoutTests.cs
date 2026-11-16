using Maple2.Model.Enum;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// UserChat read the way the client's chat line reader does (FUN_1407a5500 of Unk_1407a5330): int64 account, int64
// character, unicode name, byte "text is a string id" (0: a unicode text; 1: an int string id), int type, byte GM
// flag, int channel, then per type: 3 a unicode string (the whisper's picture), 0x14 an int64 (club id), 0x10 and
// 0x19 an int; a last byte. The wedding line (0x19) is followed by the reservation block (FUN_1407a5960): int64
// marriage id, int package, int hall, int64 ceremony time, unicode groom, unicode bride.
public class ChatLineLayoutTests {
    private record Line(long Account, long Character, string Name, bool IsStringId, string Text, int StringId, int Type, bool Gm, int Channel);

    private static Line ReadLine(ByteReader reader) {
        long account = reader.ReadLong();
        long character = reader.ReadLong();
        string name = reader.ReadUnicodeString();
        bool isStringId = reader.ReadBool();
        string text = string.Empty;
        int stringId = 0;
        if (isStringId) {
            stringId = reader.ReadInt();
        } else {
            text = reader.ReadUnicodeString();
        }
        int type = reader.ReadInt();
        bool gm = reader.ReadBool();
        int channel = reader.ReadInt();
        switch (type) {
            case 3:
                reader.ReadUnicodeString();
                break;
            case 0x14:
                reader.ReadLong();
                break;
            case 0x10:
            case 0x19:
                reader.ReadInt();
                break;
        }
        reader.ReadByte();
        return new Line(account, character, name, isStringId, text, stringId, type, gm, channel);
    }

    private static ByteReader Open(ByteWriter writer) {
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.UserChat));
        return reader;
    }

    [TestCase(ChatType.Normal)]
    [TestCase(ChatType.Party)]
    [TestCase(ChatType.World)]
    [TestCase(ChatType.Super)]
    [TestCase(ChatType.Club)]
    [TestCase(ChatType.WhisperFrom)]
    public void MessageLines_ReadToTheEnd(ChatType type) {
        ByteReader reader = Open(ChatPacket.Message(11, 22, "Nm", type, "hello", gm: true, channel: 5));
        Line line = ReadLine(reader);
        Assert.Multiple(() => {
            Assert.That(line, Is.EqualTo(new Line(11, 22, "Nm", false, "hello", 0, (int) type, true, 5)));
            Assert.That(reader.Available, Is.Zero);
        });
    }

    [Test]
    public void WhisperLines_ReadToTheEnd() {
        ByteReader received = Open(ChatPacket.Whisper(1, 2, "Nm", "psst", "pic", gm: true, channel: 3));
        Line line = ReadLine(received);
        Assert.That(line, Is.EqualTo(new Line(1, 2, "Nm", false, "psst", 0, 3, true, 3)));
        Assert.That(received.Available, Is.Zero);

        ByteReader own = Open(ChatPacket.Whisper(1, 2, "To", "psst"));
        Assert.That(ReadLine(own), Is.EqualTo(new Line(1, 2, "To", false, "psst", 0, 4, false, 0)));
        Assert.That(own.Available, Is.Zero);

        // The refusal line names the refuser and carries string id 0.
        ByteReader reject = Open(ChatPacket.WhisperReject(1, 2, "Nm", 6));
        Assert.That(ReadLine(reject), Is.EqualTo(new Line(1, 2, "Nm", true, "", 0, 6, false, 6)));
        Assert.That(reject.Available, Is.Zero);
    }

    [Test]
    public void SystemAndAlertLines_ReadToTheEnd() {
        ByteReader system = Open(ChatPacket.System("Tag", "text"));
        Assert.That(ReadLine(system), Is.EqualTo(new Line(0, 0, "Tag", false, "text", 0, 19, false, 0)));
        Assert.That(system.Available, Is.Zero);

        ByteReader alert = Open(ChatPacket.Alert("text"));
        Assert.That(ReadLine(alert), Is.EqualTo(new Line(0, 0, "", false, "text", 0, 17, false, 0)));
        Assert.That(alert.Available, Is.Zero);

        ByteReader coded = Open(ChatPacket.Alert(StringCode.s_item_err_puton_pcbang));
        Line line = ReadLine(coded);
        Assert.That(line.IsStringId, Is.True);
        Assert.That(line.Type, Is.EqualTo(17));
        Assert.That(coded.Available, Is.Zero);
    }

    [Test]
    public void WeddingLine_IsFollowedByTheReservationBlock() {
        ByteReader reader = Open(ChatPacket.Wedding(1, 2, "Nm", "vows", 20300001, 0x0102030405, 7, 8, 1_700_000_000, "Groom", "Bride", channel: 2));
        Line line = ReadLine(reader);
        Assert.That(line.Type, Is.EqualTo(0x19));
        Assert.That(reader.ReadLong(), Is.EqualTo(0x0102030405));
        Assert.That(reader.ReadInt(), Is.EqualTo(7));
        Assert.That(reader.ReadInt(), Is.EqualTo(8));
        Assert.That(reader.ReadLong(), Is.EqualTo(1_700_000_000));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Groom"));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Bride"));
        Assert.That(reader.Available, Is.Zero);
    }
}
