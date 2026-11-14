using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Tools.Extensions;

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

    [TestCase((byte) 0, 0, true)]
    [TestCase((byte) 1, 0xFFFF, true)]
    [TestCase((byte) 2, 1, false)]
    [TestCase((byte) 1, 0x10000, false)]
    [TestCase((byte) 1, -1, false)]
    public void FocusIsAListedFlagAndSixteenBits(byte listed, int focus, bool expected) {
        Assert.That(GuildRules.IsFocus(listed, focus), Is.EqualTo(expected));
    }

    [TestCase(0x7FFFFFFF, true)]
    [TestCase(1, true)]
    [TestCase(0xFFFF, true)]
    [TestCase(0, false)]
    [TestCase(0x10000, false)]
    [TestCase(-1, false)]
    public void SearchFocusIsSomeFocusBitsOrAll(int focus, bool expected) {
        Assert.That(GuildRules.IsSearchFocus(focus), Is.EqualTo(expected));
    }

    [Test]
    public void SearchNameNeedsTwoCharacters() {
        Assert.That(GuildRules.IsSearchName("a"), Is.False);
        Assert.That(GuildRules.IsSearchName("ab"), Is.True);
        Assert.That(GuildRules.IsSearchName(new string('a', 51)), Is.False);
    }

    [TestCase(1001, 1001, 500, 620, 120)] // own check-in or donation: the rise counts
    [TestCase(1001, 1002, 500, 620, 0)] // another member's contribution
    [TestCase(1001, 1001, 620, 620, 0)] // funds only, no exp
    public void ContributedExp_CountsOnlyTheContributorsRise(long characterId, long contributorId, int oldExp, int newExp, long expected) {
        Assert.That(GuildRules.ContributedExp(characterId, contributorId, oldExp, newExp), Is.EqualTo(expected));
    }

    // A guild quest's guildFund: added up to the level's fundMax, never past int.MaxValue, and a fund already at
    // or over the ceiling stays as it is.
    [TestCase(100, 50, 1000L, 150)]
    [TestCase(980, 50, 1000L, 1000)]
    [TestCase(1200, 50, 1000L, 1200)]
    [TestCase(100, 0, 1000L, 100)]
    [TestCase(int.MaxValue - 5, 50, 5_000_000_000L, int.MaxValue)]
    public void AddFund_StopsAtTheFundMax(int funds, int add, long fundMax, int expected) {
        Assert.That(GuildRules.AddFund(funds, add, fundMax), Is.EqualTo(expected));
    }

    // Guild 84 with accepted 0: the applicant's mail is the client's reject mail, with the guild's name as the
    // {0} of the title and the text, read back as Mail::Read gets it.
    [Test]
    public void RejectedApplicationMail_IsTheClientRejectMail() {
        Mail mail = GuildRules.RejectedApplicationMail(7, "A&B");
        Assert.That(mail.ReceiverId, Is.EqualTo(7));

        var writer = new ByteWriter();
        writer.WriteClass<Mail>(mail);
        var reader = new ByteReader(writer.Buffer, 0);
        Assert.That(reader.ReadByte(), Is.EqualTo((byte) MailType.System));
        reader.ReadLong(); // id
        Assert.That(reader.ReadLong(), Is.EqualTo(0), "no sender character");
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("""<ms2><v key="s_guild_search_join_reject_mail_sender" /></ms2>"""));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("""<ms2><v key="s_guild_search_join_reject_mail_title" /></ms2>"""));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("""<ms2><v key="s_guild_search_join_reject_mail_content" /></ms2>"""));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("""<ms2><v str="A&amp;B" /></ms2>"""));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("""<ms2><v str="A&amp;B" /></ms2>"""));
        Assert.That(reader.ReadByte(), Is.EqualTo(0), "no items");
    }

    // A guild quest's contribution kind by its basic repeatable: 2 daily, 3 weekly, any other none; other quest types none.
    [TestCase(QuestType.GuildQuest, 2, GuildContributionKind.DailyQuest)]
    [TestCase(QuestType.GuildQuest, 3, GuildContributionKind.WeeklyQuest)]
    [TestCase(QuestType.GuildQuest, 0, null)]
    [TestCase(QuestType.GuildQuest, 1, null)]
    [TestCase(QuestType.EpicQuest, 2, null)]
    [TestCase(QuestType.FieldQuest, 3, null)]
    public void QuestContributionKind_ByRepeat(QuestType type, int repeat, string? kind) {
        Assert.That(GuildRules.QuestContributionKind(type, repeat), Is.EqualTo(kind));
    }
}
