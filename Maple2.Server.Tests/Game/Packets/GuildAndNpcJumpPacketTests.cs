using System.Linq;
using System.Numerics;
using Maple2.Model;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// The layouts are the client's readers: CGuildManager::OnPacketMyDonation (mode 113),
// CGuildJoinRequestForChar::ReadStream (mode 86) and the jump block of CNpc::ApplySyncBlock.
public class GuildAndNpcJumpPacketTests {
    private static PlayerInfo Info(string name) =>
        new(new CharacterInfo(1, 2, name, "", "", Gender.Male, Job.Newbie, 1), "", new AchievementInfo(), []);

    // CGuildManager::OnPacketWeeklyContributionReset (mode 61) reads no body.
    [Test]
    public void WeeklyContributionReset_IsMode61WithNoBody() {
        ByteWriter writer = GuildPacket.WeeklyContributionReset();
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Guild));
        Assert.That(reader.ReadByte(), Is.EqualTo(61));
        Assert.That(writer.Length, Is.EqualTo(3));
    }

    // The weekly reset zeroes each member's weekly contribution and keeps the total.
    [Test]
    public void ResetWeeklyContribution_KeepsTheTotal() {
        var members = new[] {
            new GuildMember { Info = Info("A"), WeeklyContribution = 30, TotalContribution = 120 },
            new GuildMember { Info = Info("B"), WeeklyContribution = 0, TotalContribution = 50 },
        };

        Assert.That(GuildRules.ResetWeeklyContribution(members), Is.EqualTo(1));
        Assert.That(members.Select(member => (member.WeeklyContribution, member.TotalContribution)),
            Is.EqualTo(new[] { (0, 120), (0, 50) }));
    }

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

    // CGuildInfo::Read: the byte after the creation time is the championship state (0 in KMS2), then the score
    // 1000; the npc shop block before the storage list is a set flag and an empty list, as KMS2 sends it.
    [Test]
    public void Load_SendsNoChampionshipStateAndAnEmptyNpcShopList() {
        var guild = new Guild(7, "Guild", 1, 2, "Leader") { CreationTime = 0x0102030405060708 };
        ByteWriter writer = GuildPacket.Load(guild);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Guild));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadLong(), Is.EqualTo(7));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Guild"));
        reader.ReadUnicodeString();
        reader.ReadByte();
        reader.ReadUnicodeString();
        reader.ReadUnicodeString();
        Assert.That(reader.ReadLong(), Is.EqualTo(1));
        Assert.That(reader.ReadLong(), Is.EqualTo(2));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Leader"));
        Assert.That(reader.ReadLong(), Is.EqualTo(0x0102030405060708));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(1000));

        byte[] tail = writer.ToArray()[^7..];
        Assert.That(tail, Is.EqualTo(new byte[] { 1, 0, 0, 0, 0, 0, 0 }));
    }

    // CGuildManager::FUN_1413c5360 (0x52) and FUN_1413c5440 (0x53) read a long, then the guild name
    // (82: the new request's id; 83: the id that was withdrawn).
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

    // CGuildManager::FUN_1413c39e0 (0x39): the name, the house level, the theme.
    [Test]
    public void UpgradeHouse_IsMode57WithNameLevelAndTheme() {
        ByteWriter writer = GuildPacket.UpgradeHouse("Master", 2, 3);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Guild));
        Assert.That(reader.ReadByte(), Is.EqualTo(57));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Master"));
        Assert.That(reader.ReadInt(), Is.EqualTo(2));
        Assert.That(reader.ReadInt(), Is.EqualTo(3));
        Assert.That(reader.Position, Is.EqualTo(writer.Length));
    }

    // CGuildManager::FUN_1413c3ef0 (0x3b): the name, then CGuildNpc::ReadPacket (kind, level).
    [Test]
    public void UpgradeNpc_IsMode59WithNameKindAndLevel() {
        ByteWriter writer = GuildPacket.UpgradeNpc("Master", new GuildNpc(GuildNpcType.Goods, 3));
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Guild));
        Assert.That(reader.ReadByte(), Is.EqualTo(59));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Master"));
        Assert.That(reader.ReadInt(), Is.EqualTo(2));
        Assert.That(reader.ReadInt(), Is.EqualTo(3));
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

    // CGuildManager::FUN_1413c3170 (0x36): the name, then CGuildSkill::Read (id, level, time of use).
    [Test]
    public void NotifyUseBuff_IsMode54WithNameAndSkill() {
        ByteWriter writer = GuildPacket.NotifyUseBuff("Master", new GuildBuff(2, 3, 0x0102030405060708));
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Guild));
        Assert.That(reader.ReadByte(), Is.EqualTo(54));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Master"));
        Assert.That(reader.ReadInt(), Is.EqualTo(2));
        Assert.That(reader.ReadInt(), Is.EqualTo(3));
        Assert.That(reader.ReadLong(), Is.EqualTo(0x0102030405060708));
        Assert.That(reader.Position, Is.EqualTo(writer.Length));
    }

    // CGuildManager::FUN_1413bf340 (0x5a): the skill id.
    [Test]
    public void UseBuff_IsMode90WithSkillId() {
        ByteWriter writer = GuildPacket.UseBuff(2);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Guild));
        Assert.That(reader.ReadByte(), Is.EqualTo(90));
        Assert.That(reader.ReadInt(), Is.EqualTo(2));
        Assert.That(reader.Position, Is.EqualTo(writer.Length));
    }

    // CGuildManager::FUN_1413bf540 (0x5b): the skill id.
    [Test]
    public void UsePersonalBuff_IsMode91WithSkillId() {
        ByteWriter writer = GuildPacket.UsePersonalBuff(10002);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Guild));
        Assert.That(reader.ReadByte(), Is.EqualTo(91));
        Assert.That(reader.ReadInt(), Is.EqualTo(10002));
        Assert.That(reader.Position, Is.EqualTo(writer.Length));
    }

    // CGuildManager::OnPacketPvpHistory (0x4a): the row count; no rows.
    [Test]
    public void PvpHistory_IsMode74WithZeroRows() {
        ByteWriter writer = GuildPacket.PvpHistory();
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Guild));
        Assert.That(reader.ReadByte(), Is.EqualTo(74));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(reader.Position, Is.EqualTo(writer.Length));
    }

    // CGuildManager::OnPacketPvpSeasonHistory (0x4b): the row count; no rows.
    [Test]
    public void PvpSeason_IsMode75WithZeroRows() {
        ByteWriter writer = GuildPacket.PvpSeason();
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Guild));
        Assert.That(reader.ReadByte(), Is.EqualTo(75));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(reader.Position, Is.EqualTo(writer.Length));
    }

    // CGuildManager::FUN_1413c3780 (0x38): the name, the event id, the time of opening.
    [Test]
    public void NotifyOpenEvent_IsMode56WithNameIdTime() {
        ByteWriter writer = GuildPacket.NotifyOpenEvent("Opener", new GuildEvent(3, 1700000000));
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Guild));
        Assert.That(reader.ReadByte(), Is.EqualTo(56));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Opener"));
        Assert.That(reader.ReadInt(), Is.EqualTo(3));
        Assert.That(reader.ReadInt(), Is.EqualTo(1700000000));
        Assert.That(reader.Position, Is.EqualTo(writer.Length));
    }

    // CGuildManager::OnPacketOpenEvent (0x63): the event id.
    [Test]
    public void OpenEvent_IsMode99WithId() {
        ByteWriter writer = GuildPacket.OpenEvent(3);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Guild));
        Assert.That(reader.ReadByte(), Is.EqualTo(99));
        Assert.That(reader.ReadInt(), Is.EqualTo(3));
        Assert.That(reader.Position, Is.EqualTo(writer.Length));
    }

    // CGuildManager::OnPacketStorageGiftResult (0x6d): the result byte, and the receiver only for 0.
    [Test]
    public void StorageGift_IsMode109_ReceiverOnlyOnSuccess() {
        ByteWriter writer = GuildPacket.StorageGift(GuildError.s_guild_err_unknown, "ignored");
        var reader = new ByteReader(writer.Buffer, 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Guild));
        Assert.That(reader.ReadByte(), Is.EqualTo(109));
        Assert.That(reader.ReadByte(), Is.EqualTo((byte) GuildError.s_guild_err_unknown));
        Assert.That(reader.Position, Is.EqualTo(writer.Length));

        writer = GuildPacket.StorageGift(GuildError.none, "B");
        reader = new ByteReader(writer.Buffer, 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Guild));
        Assert.That(reader.ReadByte(), Is.EqualTo(109));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("B"));
        Assert.That(reader.Position, Is.EqualTo(writer.Length));
    }

    // Guild 115 is answered with the error packet (mode 79): byte 1, the error byte 47, int 0.
    [Test]
    public void VsGameRegister_IsRefusedWithError47() {
        Assert.That(GuildRules.CheckVsGameRegister(true, false), Is.EqualTo(GuildError.s_guild_err_isNotVsGameTime));
        Assert.That(GuildRules.CheckVsGameRegister(false, false), Is.EqualTo(GuildError.s_guild_err_no_authority));
        Assert.That(GuildRules.CheckVsGameRegister(false, true), Is.EqualTo(GuildError.s_guild_err_no_authority));
        Assert.That(GuildRules.CheckVsGameRegister(true, true), Is.EqualTo(GuildError.none));

        ByteWriter writer = GuildPacket.Error(GuildRules.CheckVsGameRegister(true, false));
        var reader = new ByteReader(writer.Buffer, 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Guild));
        Assert.That(reader.ReadByte(), Is.EqualTo(79));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadByte(), Is.EqualTo(47));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(reader.Position, Is.EqualTo(writer.Length));
    }

    // Guild 79 for a guild mail inside the server's 60 seconds: error 45, which the client shows as
    // "Please try again later." (guildmanager.cpp:3351), not 1 ("Unknown Guild Error", :3234).
    [Test]
    public void MailCooldown_IsRefusedWithError45() {
        ByteWriter writer = GuildPacket.Error(GuildRules.MailCooldownError);
        var reader = new ByteReader(writer.Buffer, 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Guild));
        Assert.That(reader.ReadByte(), Is.EqualTo(79));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadByte(), Is.EqualTo(45));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(reader.Position, Is.EqualTo(writer.Length));
    }

    // CGuildManager::OnPacketVsGameTicket (0x76): the ticket flag, then the notify flag.
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    public void VsGameTicket_IsMode118WithTwoBools(bool registered, bool notify) {
        ByteWriter writer = GuildPacket.VsGameTicket(registered, notify);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Guild));
        Assert.That(reader.ReadByte(), Is.EqualTo(118));
        Assert.That(reader.ReadBool(), Is.EqualTo(registered));
        Assert.That(reader.ReadBool(), Is.EqualTo(notify));
        Assert.That(reader.Position, Is.EqualTo(writer.Length));
    }

    // CGuildManager::FUN_1413c45e0 (0x77): the game id; 0 opens no prompt.
    [TestCase(0)]
    [TestCase(7)]
    public void VsGameOpen_IsMode119WithTheGameId(int gameId) {
        ByteWriter writer = GuildPacket.VsGameOpen(gameId);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Guild));
        Assert.That(reader.ReadByte(), Is.EqualTo(119));
        Assert.That(reader.ReadInt(), Is.EqualTo(gameId));
        Assert.That(reader.Position, Is.EqualTo(writer.Length));
    }

    // The guild's event list has a byte count: 300 events write 255 rows, not 300 after a count of 44.
    [Test]
    public void GuildEvents_WriteNoMoreRowsThanTheByteCount() {
        int Length(int events) {
            var guild = new Guild(20, "Guild", 31, 32, "Leader");
            for (int i = 1; i <= events; i++) {
                guild.Events.Add(new GuildEvent(i, 0));
            }
            var writer = new ByteWriter();
            guild.WriteTo(writer);
            return writer.Length;
        }

        int empty = Length(0);
        Assert.That(Length(2) - empty, Is.EqualTo(2 * 8));
        Assert.That(Length(255) - empty, Is.EqualTo(255 * 8));
        Assert.That(Length(300) - empty, Is.EqualTo(255 * 8));
    }

    // CGuildManager::OnPacketStorageHistory (0x70): the row count; no rows.
    [Test]
    public void StorageHistory_IsMode112WithZeroRows() {
        ByteWriter writer = GuildPacket.StorageHistory();
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Guild));
        Assert.That(reader.ReadByte(), Is.EqualTo(112));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
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
