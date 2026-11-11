using System.Numerics;
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

    [Test]
    public void ListAppliedGuilds_WritesWhatTheClientReads() {
        var guild = new Guild(20, "Guild", 31, 32, "Leader") {
            Emblem = "emblem",
            Experience = 4500,
            Capacity = 60,
            AchievementInfo = new AchievementInfo { Combat = 1, Adventure = 2, Lifestyle = 3 },
        };
        var application = new GuildApplication { Id = 10, Guild = guild, Applicant = Info("Applicant"), CreationTime = 99 };
        ByteWriter writer = GuildPacket.ListAppliedGuilds([application]);
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
        Assert.That(reader.ReadInt(), Is.EqualTo(0)); // members
        Assert.That(reader.ReadInt(), Is.EqualTo(60)); // capacity
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Leader"));
        Assert.That(reader.ReadLong(), Is.EqualTo(1)); // applicant account
        Assert.That(reader.ReadLong(), Is.EqualTo(2)); // applicant character
        Assert.That(reader.ReadLong(), Is.EqualTo(99));
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
