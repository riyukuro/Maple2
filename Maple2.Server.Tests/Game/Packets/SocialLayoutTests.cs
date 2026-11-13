using Maple2.Model;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// Byte layouts of the social s2c writers, read in the client's order: CGuildJoinRequestForGuild::ReadStream
// (guilddef_1.cpp), CGuildManager modes 0x1b and 0x43 (guildmanager.cpp), the UserChat line tails
// (uimessenger.cpp), Buddy invite and Mail notify.
public class SocialLayoutTests {
    private static PlayerInfo Applicant() =>
        new(new CharacterInfo(11, 22, "Applicant", "", "pic", Gender.Female, Job.Knight, 45), "", new AchievementInfo(), []);

    private static GuildApplication Application() => new() {
        Id = 5,
        Guild = new Guild(7, "Guild", 1, 2, "Leader"),
        Applicant = Applicant(),
        CreationTime = 0x0102030405060708,
    };

    private static void ReadApplication(ByteReader reader) {
        Assert.That(reader.ReadLong(), Is.EqualTo(5));
        Assert.That(reader.ReadLong(), Is.EqualTo(7));
        Assert.That(reader.ReadLong(), Is.EqualTo(11));
        Assert.That(reader.ReadLong(), Is.EqualTo(22));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Applicant"));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("pic"));
        // SetFromJoinRequest: the first int is the job code, the second the job.
        Assert.That(reader.ReadInt(), Is.EqualTo((int) JobCode.Knight));
        Assert.That(reader.ReadInt(), Is.EqualTo((int) Job.Knight));
        Assert.That(reader.ReadInt(), Is.EqualTo(45));
        reader.ReadInt();
        reader.ReadInt();
        reader.ReadInt();
        Assert.That(reader.ReadLong(), Is.EqualTo(0x0102030405060708));
    }

    [Test]
    public void ReceiveApplication_WritesTheJobCodeBeforeTheJob() {
        ByteWriter writer = GuildPacket.ReceiveApplication(Application());
        var reader = new ByteReader(writer.ToArray(), 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Guild));
        Assert.That(reader.ReadByte(), Is.EqualTo(0x2d));
        ReadApplication(reader);
        Assert.That(reader.Available, Is.Zero);
    }

    [Test]
    public void ListApplications_IsAFlagBeforeEachEntry() {
        ByteWriter writer = GuildPacket.ListApplications([Application(), Application()]);
        var reader = new ByteReader(writer.ToArray(), 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Guild));
        Assert.That(reader.ReadByte(), Is.EqualTo(0x55));
        Assert.That(reader.ReadInt(), Is.EqualTo(2));
        for (int i = 0; i < 2; i++) {
            Assert.That(reader.ReadBool(), Is.True);
            ReadApplication(reader);
        }
        Assert.That(reader.Available, Is.Zero);
    }

    // FUN_1413c15b0: StringIdInterface (bool formatted, i32 kind, then the plain text), then the emblem code.
    [Test]
    public void NotifyUpdateEmblem_IsTheTextThenTheCode() {
        ByteWriter writer = GuildPacket.NotifyUpdateEmblem("Changer", "emblem");
        var reader = new ByteReader(writer.ToArray(), 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Guild));
        Assert.That(reader.ReadByte(), Is.EqualTo(0x1b));
        Assert.That(reader.ReadBool(), Is.False);
        reader.ReadInt();
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Changer"));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("emblem"));
        Assert.That(reader.Available, Is.Zero);
    }

    // Mode 0x43: the grade index, then CGuildGrade (u8 id, name, i32 rights).
    [Test]
    public void UpdateRank_IsTheIndexThenTheGrade() {
        var rank = new GuildRank { Id = 3, Name = "Rank", Permission = (GuildPermission) 0x15 };
        ByteWriter writer = GuildPacket.UpdateRank(rank);
        var reader = new ByteReader(writer.ToArray(), 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Guild));
        Assert.That(reader.ReadByte(), Is.EqualTo(0x43));
        Assert.That(reader.ReadByte(), Is.EqualTo(3));
        Assert.That(reader.ReadByte(), Is.EqualTo(3));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Rank"));
        Assert.That(reader.ReadInt(), Is.EqualTo(0x15));
        Assert.That(reader.Available, Is.Zero);
    }

    // Modes 0x36/0x37: the user, then CGuildSkill (i32 id, i32 level, i64 expiry).
    [Test]
    public void UpgradeBuff_IsTheNameThenTheSkill() {
        ByteWriter writer = GuildPacket.UpgradeBuff("User", new GuildBuff(9, 2, 0x0102030405060708));
        var reader = new ByteReader(writer.ToArray(), 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Guild));
        Assert.That(reader.ReadByte(), Is.EqualTo(0x37));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("User"));
        Assert.That(reader.ReadInt(), Is.EqualTo(9));
        Assert.That(reader.ReadInt(), Is.EqualTo(2));
        Assert.That(reader.ReadLong(), Is.EqualTo(0x0102030405060708));
        Assert.That(reader.Available, Is.Zero);
    }

    // The club line carries the club id after the channel, then the line's end flag.
    [Test]
    public void ChatMessage_ClubLineEndsWithTheClubIdAndTheFlag() {
        ByteWriter writer = ChatPacket.Message(1, 2, "Name", ChatType.Club, "text", clubId: 0x0102030405060708, channel: 4);
        var reader = new ByteReader(writer.ToArray(), 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.UserChat));
        Assert.That(reader.ReadLong(), Is.EqualTo(1));
        Assert.That(reader.ReadLong(), Is.EqualTo(2));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Name"));
        Assert.That(reader.ReadBool(), Is.False);
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("text"));
        Assert.That(reader.Read<ChatType>(), Is.EqualTo(ChatType.Club));
        Assert.That(reader.ReadBool(), Is.False);
        Assert.That(reader.ReadInt(), Is.EqualTo(4));
        Assert.That(reader.ReadLong(), Is.EqualTo(0x0102030405060708));
        Assert.That(reader.ReadBool(), Is.False);
        Assert.That(reader.Available, Is.Zero);
    }

    // A received whisper carries the sender's picture after the channel; the own copy has no picture.
    [Test]
    public void Whisper_ReceivedLineCarriesThePicture() {
        ByteWriter received = ChatPacket.Whisper(1, 2, "Name", "text", "pic", channel: 3);
        var reader = new ByteReader(received.ToArray(), 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.UserChat));
        reader.ReadLong();
        reader.ReadLong();
        reader.ReadUnicodeString();
        reader.ReadBool();
        reader.ReadUnicodeString();
        Assert.That(reader.Read<ChatType>(), Is.EqualTo(ChatType.WhisperFrom));
        reader.ReadBool();
        Assert.That(reader.ReadInt(), Is.EqualTo(3));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("pic"));
        Assert.That(reader.ReadBool(), Is.False);
        Assert.That(reader.Available, Is.Zero);

        ByteWriter own = ChatPacket.Whisper(1, 2, "Name", "text");
        reader = new ByteReader(own.ToArray(), 0);
        reader.Read<SendOp>();
        reader.ReadLong();
        reader.ReadLong();
        reader.ReadUnicodeString();
        reader.ReadBool();
        reader.ReadUnicodeString();
        Assert.That(reader.Read<ChatType>(), Is.EqualTo(ChatType.WhisperTo));
        reader.ReadBool();
        Assert.That(reader.ReadInt(), Is.Zero);
        Assert.That(reader.ReadBool(), Is.False);
        Assert.That(reader.Available, Is.Zero);
    }

    [Test]
    public void BuddyInvite_IsTheErrorThenNameAndMessage() {
        ByteWriter writer = BuddyPacket.Invite("Name", "hello");
        var reader = new ByteReader(writer.ToArray(), 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Buddy));
        reader.ReadByte();
        Assert.That(reader.Read<BuddyError>(), Is.EqualTo(BuddyError.ok));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Name"));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("hello"));
        Assert.That(reader.Available, Is.Zero);
    }

    [Test]
    public void MailNotify_IsTheCountsAroundTheAlertFlag() {
        ByteWriter writer = MailPacket.Notify(3, true, 2);
        var reader = new ByteReader(writer.ToArray(), 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Mail));
        reader.ReadByte();
        Assert.That(reader.ReadInt(), Is.EqualTo(3));
        Assert.That(reader.ReadBool(), Is.True);
        Assert.That(reader.ReadInt(), Is.EqualTo(2));
        Assert.That(reader.Available, Is.Zero);
    }
}
