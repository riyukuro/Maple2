using System.Numerics;
using Maple2.Model;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// The layouts are the client's readers: CGuildManager::OnPacketMyDonation (mode 113),
// CGuildJoinRequestForChar::ReadStream (mode 86) and the jump block of CNpc::ApplySyncBlock.
public class GuildAndNpcJumpPacketTests {
    private static PlayerInfo Info(string name) =>
        new(new CharacterInfo(1, 2, name, "", "", Gender.Male, Job.Newbie, 1), "", new AchievementInfo(), []);

    [Test]
    public void Donated_IsMode113WithCountAndTime() {
        var member = new GuildMember { Info = Info("Leader"), DailyDonationCount = 3, DonationTime = 0x0102030405060708 };
        ByteWriter writer = GuildPacket.Donated(member);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Guild));
        Assert.That(reader.ReadByte(), Is.EqualTo(113));
        Assert.That(reader.ReadInt(), Is.EqualTo(3));
        Assert.That(reader.ReadLong(), Is.EqualTo(0x0102030405060708));
        Assert.That(reader.Position, Is.EqualTo(writer.Length));
    }

    // CGuildManager::FUN_1413c5360 (0x52) and FUN_1413c5440 (0x53) read the guild id, then the guild name.
    [TestCase(82)]
    [TestCase(83)]
    public void ApplicationReplies_CarryTheGuildIdAndName(byte mode) {
        const long guildId = 0x1122334455667788;
        ByteWriter writer = mode == 82 ? GuildPacket.SendApplication(guildId, "Guild") : GuildPacket.CancelApplication(guildId, "Guild");
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Guild));
        Assert.That(reader.ReadByte(), Is.EqualTo(mode));
        Assert.That(reader.ReadLong(), Is.EqualTo(guildId));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Guild"));
        Assert.That(reader.Position, Is.EqualTo(writer.Length));
    }

    // CGuildManager::FUN_1413c3460 (0x37): the name, then CGuildSkill::Read (id, level, expiry time).
    [Test]
    public void UpgradeBuff_IsMode55WithNameAndSkill() {
        ByteWriter writer = GuildPacket.UpgradeBuff("Master", new GuildBuff(10002, 3, 0x0102030405060708));
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Guild));
        Assert.That(reader.ReadByte(), Is.EqualTo(55));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Master"));
        Assert.That(reader.ReadInt(), Is.EqualTo(10002));
        Assert.That(reader.ReadInt(), Is.EqualTo(3));
        Assert.That(reader.ReadLong(), Is.EqualTo(0x0102030405060708));
        Assert.That(reader.Position, Is.EqualTo(writer.Length));
    }

    [Test]
    public void SendMail_IsMode71WithNoBody() {
        ByteWriter writer = GuildPacket.SendMail();
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Guild));
        Assert.That(reader.ReadByte(), Is.EqualTo(71));
        Assert.That(reader.Position, Is.EqualTo(writer.Length));
    }

    [Test]
    public void ListAppliedGuilds_WritesWhatTheClientReads() {
        var guild = new Guild(20, "Guild", 31, 32, "Leader") {
            Emblem = "emblem",
            Experience = 4500,
            Capacity = 60,
            AchievementInfo = new AchievementInfo { Combat = 1, Adventure = 2, Lifestyle = 3 },
        };
        var application = new GuildApplication { Id = 10, Guild = guild, Applicant = Info("Applicant"), CreationTime = 99 };
        ByteWriter writer = GuildPacket.ListAppliedGuilds([(application, 7)]);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Guild));
        Assert.That(reader.ReadByte(), Is.EqualTo(86));
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadLong(), Is.EqualTo(10));
        Assert.That(reader.ReadLong(), Is.EqualTo(20));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Guild"));
        Assert.That(reader.ReadInt(), Is.EqualTo(4500)); // experience
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("emblem"));
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo(2));
        Assert.That(reader.ReadInt(), Is.EqualTo(3));
        Assert.That(reader.ReadInt(), Is.EqualTo(7)); // members
        Assert.That(reader.ReadInt(), Is.EqualTo(60)); // capacity
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Leader"));
        Assert.That(reader.ReadLong(), Is.EqualTo(1)); // applicant account
        Assert.That(reader.ReadLong(), Is.EqualTo(2)); // applicant character
        Assert.That(reader.ReadLong(), Is.EqualTo(99));
        Assert.That(reader.Position, Is.EqualTo(writer.Length));
    }

    // CGuildJoinRequestForGuild::ReadStream after CGuildManager::OnPacketJoinRequestsForGuild's count and
    // per-entry byte. The fourth id is the one the accept page sends back in Guild 84: the character id.
    [Test]
    public void ListApplications_WritesWhatTheClientReads() {
        var guild = new Guild(20, "Guild", 31, 32, "Leader");
        var applicant = new PlayerInfo(new CharacterInfo(1, 2, "Applicant", "", "pic", Gender.Male, Job.Wizard, 35), "",
            new AchievementInfo { Combat = 1, Adventure = 2, Lifestyle = 3 }, []);
        var application = new GuildApplication { Id = 10, Guild = guild, Applicant = applicant, CreationTime = 99 };
        ByteWriter writer = GuildPacket.ListApplications([application]);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Guild));
        Assert.That(reader.ReadByte(), Is.EqualTo(85));
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadLong(), Is.EqualTo(10)); // request
        Assert.That(reader.ReadLong(), Is.EqualTo(20)); // guild
        Assert.That(reader.ReadLong(), Is.EqualTo(1)); // account
        Assert.That(reader.ReadLong(), Is.EqualTo(2)); // character
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Applicant"));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("pic"));
        Assert.That(reader.ReadInt(), Is.EqualTo((int) Job.Wizard));
        Assert.That(reader.ReadInt(), Is.EqualTo((int) Job.Wizard.Code()));
        Assert.That(reader.ReadInt(), Is.EqualTo(35));
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo(2));
        Assert.That(reader.ReadInt(), Is.EqualTo(3));
        Assert.That(reader.ReadLong(), Is.EqualTo(99));
        Assert.That(reader.Position, Is.EqualTo(writer.Length));
    }

    // CGuildManager::OnPacketJoinRequestAnswer: character id, the applicant's name, accepted.
    [Test]
    public void RespondApplication_NamesTheApplicant() {
        ByteWriter writer = GuildPacket.RespondApplication(2, "Applicant", true);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Guild));
        Assert.That(reader.ReadByte(), Is.EqualTo(84));
        Assert.That(reader.ReadLong(), Is.EqualTo(2));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Applicant"));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.Position, Is.EqualTo(writer.Length));
    }

    // CGuildManager::OnPacketMyJoinRequestRemoved: guild name, request id, joined.
    [Test]
    public void NotifyApplicant_WritesWhatTheClientReads() {
        ByteWriter writer = GuildPacket.NotifyApplicant("Guild", 10, true);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Guild));
        Assert.That(reader.ReadByte(), Is.EqualTo(48));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Guild"));
        Assert.That(reader.ReadLong(), Is.EqualTo(10));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.Position, Is.EqualTo(writer.Length));
    }

    // CSearchGuildJoin::ReadStream after CGuildManager::OnPacketSearchResults' count and per-entry byte.
    [Test]
    public void ListGuilds_WritesWhatTheClientReads() {
        var entry = new GuildSearchEntry(20, "Guild", "emblem", 7, 4500, 31, 32, "Leader");
        ByteWriter writer = GuildPacket.ListGuilds([(entry, 60)]);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Guild));
        Assert.That(reader.ReadByte(), Is.EqualTo(87));
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadLong(), Is.EqualTo(20));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Guild"));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("emblem"));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(7)); // members
        Assert.That(reader.ReadInt(), Is.EqualTo(60)); // capacity
        Assert.That(reader.ReadInt(), Is.EqualTo(4500)); // experience
        Assert.That(reader.ReadLong(), Is.EqualTo(31));
        Assert.That(reader.ReadLong(), Is.EqualTo(32));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Leader"));
        Assert.That(reader.Position, Is.EqualTo(writer.Length));
    }

    [Test]
    public void ListGuilds_EmptyIsOnlyTheCount() {
        ByteWriter writer = GuildPacket.ListGuilds([]);
        Assert.That(writer.Length, Is.EqualTo(2 + 1 + 4));
    }

    // CGuildManager::FUN_1413bf2a0 (own reply) and FUN_1413c1c50 (to the members).
    [Test]
    public void FocusPackets_AreAByteAndAnInt() {
        ByteWriter writer = GuildPacket.UpdateFocus(false, GuildFocus.Dungeons | GuildFocus.Weekends);
        var reader = new ByteReader(writer.Buffer, 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Guild));
        Assert.That(reader.ReadByte(), Is.EqualTo(68));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(8 | 1024));
        Assert.That(reader.Position, Is.EqualTo(writer.Length));

        writer = GuildPacket.NotifyUpdateFocus("Leader", true, GuildFocus.Social);
        reader = new ByteReader(writer.Buffer, 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Guild));
        Assert.That(reader.ReadByte(), Is.EqualTo(30));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Leader"));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.Position, Is.EqualTo(writer.Length));
    }

    [Test]
    public void NpcJump_RelativeIsAByteAndOnePoint() {
        var writer = new ByteWriter();
        writer.WriteJump(isAbsolute: false, default, new Vector3(1, 2, 3), 0, 0);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.Read<Vector3>(), Is.EqualTo(new Vector3(1, 2, 3)));
        Assert.That(reader.Position, Is.EqualTo(writer.Length));
        Assert.That(writer.Length, Is.EqualTo(13));
    }

    [Test]
    public void NpcJump_AbsoluteIsAByteTwoPointsAndTwoFloats() {
        var writer = new ByteWriter();
        writer.WriteJump(isAbsolute: true, new Vector3(1, 2, 3), new Vector3(4, 5, 6), 7, 8);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.Read<Vector3>(), Is.EqualTo(new Vector3(1, 2, 3)));
        Assert.That(reader.Read<Vector3>(), Is.EqualTo(new Vector3(4, 5, 6)));
        Assert.That(reader.ReadFloat(), Is.EqualTo(7f));
        Assert.That(reader.ReadFloat(), Is.EqualTo(8f));
        Assert.That(reader.Position, Is.EqualTo(writer.Length));
        Assert.That(writer.Length, Is.EqualTo(33));
    }
}
