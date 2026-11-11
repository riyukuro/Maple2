using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Tests.Validators;

public class GuildRulesTests {
    // Guild 10 after the mode byte: the member's name, then the rank as one byte.
    private static (string Name, byte Rank) ReadSetRank(byte[] body) {
        var packet = new ByteReader(body, 0);
        return (packet.ReadUnicodeString(), packet.ReadByte());
    }

    private static byte[] SetRankBody(string name, byte rank) {
        var writer = new ByteWriter();
        writer.WriteUnicodeString(name);
        writer.WriteByte(rank);
        return writer.ToArray();
    }

    [Test]
    public void SetRankBytes() {
        // "ab" as 02 00 61 00 62 00, then rank 3.
        (string name, byte rank) = ReadSetRank(new byte[] { 0x02, 0x00, 0x61, 0x00, 0x62, 0x00, 0x03 });

        Assert.That(name, Is.EqualTo("ab"));
        Assert.That(rank, Is.EqualTo(3));
        Assert.That(GuildRules.IsAssignableRank(rank, 6), Is.True);
    }

    [Test]
    public void RankZeroIsNotGiven() {
        (_, byte rank) = ReadSetRank(SetRankBody("member", 0));

        Assert.That(GuildRules.IsAssignableRank(rank, 6), Is.False);
    }

    [Test]
    public void RankIsOneToTheLastIndex() {
        for (byte rank = 1; rank <= 5; rank++) {
            Assert.That(GuildRules.IsAssignableRank(rank, 6), Is.True, $"rank {rank}");
        }
        Assert.That(GuildRules.IsAssignableRank(6, 6), Is.False);
        Assert.That(GuildRules.IsAssignableRank(255, 6), Is.False);
        Assert.That(GuildRules.IsAssignableRank(1, 1), Is.False);
    }

    // Guild 64 after the mode byte: the kind as one byte, then the text.
    private static (byte Kind, string Text) ReadSetText(byte[] body) {
        var packet = new ByteReader(body, 0);
        return (packet.ReadByte(), packet.ReadUnicodeString());
    }

    private static byte[] SetTextBody(byte kind, string text) {
        var writer = new ByteWriter();
        writer.WriteByte(kind);
        writer.WriteUnicodeString(text);
        return writer.ToArray();
    }

    [Test]
    public void NoticeBytes() {
        // Kind 1, then "hi" as 02 00 68 00 69 00.
        (byte kind, string text) = ReadSetText(new byte[] { 0x01, 0x02, 0x00, 0x68, 0x00, 0x69, 0x00 });

        Assert.That(kind, Is.EqualTo(GuildRules.NoticeKind));
        Assert.That(text, Is.EqualTo("hi"));
        Assert.That(GuildRules.IsNotice(kind, text), Is.True);
    }

    [Test]
    public void OtherKindIsNotTheNotice() {
        (byte kind, string text) = ReadSetText(SetTextBody(0, "master text"));
        Assert.That(GuildRules.IsNotice(kind, text), Is.False);

        (kind, text) = ReadSetText(SetTextBody(2, "x"));
        Assert.That(GuildRules.IsNotice(kind, text), Is.False);
    }

    [Test]
    public void NoticeLengthIsTheClientLimit() {
        (byte kind, string text) = ReadSetText(SetTextBody(1, new string('a', 300)));
        Assert.That(GuildRules.IsNotice(kind, text), Is.True);

        (kind, text) = ReadSetText(SetTextBody(1, new string('a', 301)));
        Assert.That(GuildRules.IsNotice(kind, text), Is.False);

        Assert.That(GuildRules.IsNotice(1, string.Empty), Is.True);
    }

    [Test]
    public void MasterKeepsRankZero() {
        Assert.That(GuildRules.CanChangeRankOf(GuildRules.MasterRank), Is.False);
        Assert.That(GuildRules.CanChangeRankOf(1), Is.True);
        Assert.That(GuildRules.CanChangeRankOf(5), Is.True);
    }
}
