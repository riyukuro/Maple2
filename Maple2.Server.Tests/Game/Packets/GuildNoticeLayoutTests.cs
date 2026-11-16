using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Tests.Game.Packets;

// Guild sub-modes against CGuildManager::FUN_1413b6370 (client/ms2/guildmanager.cpp:303) and the readers it calls:
// 3 (invited): unicode name; 4 (invite info): CGuildInviteInfo::ReadStream (guilddef_1.cpp:578: long guild id, unicode
// guild name, unicode, unicode sender, unicode receiver); 8 and 9 (expelled, notify expel): unicode name; 23 (login):
// unicode name; 24 (logout): unicode name, long time; 36 (check in time, OnPacketMemberContribution guildmanager.cpp:2104:
// unicode name, long value kept on the member, then the attend background); 47 (FUN_1413c6180): unicode guild, unicode
// name, u8 accepted, long request id; 50 and 51 (OnPacketGuildExp, FUN_1413c4b60): int; 53 (FUN_1413c2590): unicode
// name, int gained, int weekly, int total; 97 (OnPacketGainExpFund): int exp, int funds.
public class GuildNoticeLayoutTests {
    private static ByteReader Read(ByteWriter writer) {
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.That(reader.ReadShort(), Is.EqualTo((short) SendOp.Guild));
        return reader;
    }

    [Test]
    public void NameOnlyModes() {
        foreach ((ByteWriter writer, byte mode) in new[] {
                     (GuildPacket.Invited("Ann"), (byte) 3), (GuildPacket.Expelled("Ann"), (byte) 8),
                     (GuildPacket.NotifyExpel("Ann"), (byte) 9), (GuildPacket.NotifyLogin("Ann"), (byte) 23),
                 }) {
            ByteReader reader = Read(writer);
            Assert.That(reader.ReadByte(), Is.EqualTo(mode));
            Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Ann"));
            Assert.That(reader.Available, Is.EqualTo(0));
        }
    }

    [Test]
    public void InviteInfo_Logout_CheckInTime() {
        var invite = new GuildInvite { GuildId = 5, GuildName = "Gu", SenderName = "Sa", ReceiverName = "Re" };
        ByteReader reader = Read(GuildPacket.InviteInfo(invite));
        Assert.That(reader.ReadByte(), Is.EqualTo(4));
        Assert.That(reader.ReadLong(), Is.EqualTo(5));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Gu"));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo(string.Empty));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Sa"));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Re"));
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Read(GuildPacket.NotifyLogout("Ann", 1700000000));
        Assert.That(reader.ReadByte(), Is.EqualTo(24));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Ann"));
        Assert.That(reader.ReadLong(), Is.EqualTo(1700000000));
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Read(GuildPacket.CheckInTime("Ann", 1700000500));
        Assert.That(reader.ReadByte(), Is.EqualTo(36));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Ann"));
        Assert.That(reader.ReadLong(), Is.EqualTo(1700000500));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void ApplicationAndContributionModes() {
        ByteReader reader = Read(GuildPacket.NotifyApplication("Req", "Pla", 77, true));
        Assert.That(reader.ReadByte(), Is.EqualTo(47));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Req"));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Pla"));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadLong(), Is.EqualTo(77));
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Read(GuildPacket.GuildExperience(900));
        Assert.That(reader.ReadByte(), Is.EqualTo(50));
        Assert.That(reader.ReadInt(), Is.EqualTo(900));
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Read(GuildPacket.GuildFunds(450));
        Assert.That(reader.ReadByte(), Is.EqualTo(51));
        Assert.That(reader.ReadInt(), Is.EqualTo(450));
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Read(GuildPacket.AddContribution(10, 20));
        Assert.That(reader.ReadByte(), Is.EqualTo(97));
        Assert.That(reader.ReadInt(), Is.EqualTo(10));
        Assert.That(reader.ReadInt(), Is.EqualTo(20));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void GuildContribution() {
        var info = new PlayerInfo(new CharacterInfo(101, 1, "Ann", "", "", Gender.Male, Job.Newbie, 20), "", new AchievementInfo(), []);
        var member = new GuildMember { GuildId = 5, Info = info, WeeklyContribution = 30, TotalContribution = 90 };
        ByteReader reader = Read(GuildPacket.GuildContribution(member, 10));
        Assert.That(reader.ReadByte(), Is.EqualTo(53));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Ann"));
        Assert.That(new[] { reader.ReadInt(), reader.ReadInt(), reader.ReadInt() }, Is.EqualTo(new[] { 10, 30, 90 }));
        Assert.That(reader.Available, Is.EqualTo(0));
    }
}
