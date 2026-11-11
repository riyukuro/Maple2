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

    [Test]
    public void MasterKeepsRankZero() {
        Assert.That(GuildRules.CanChangeRankOf(GuildRules.MasterRank), Is.False);
        Assert.That(GuildRules.CanChangeRankOf(1), Is.True);
        Assert.That(GuildRules.CanChangeRankOf(5), Is.True);
    }
}
