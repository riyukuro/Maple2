using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Game.Club;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// The Club packet read the way CClubClientManager::OnClub (clubcli.cpp:792) and its handlers read it, with CClub::Read,
// CClubMember::Read and CClubMemberProxy::Read (clubcommon.cpp) for the club and member blocks:
// 0 and 2 a club (int64 id, name, int64 leader account, int64 leader character, leader name, int64 created, byte state,
// int buff id, int buff level, int64 name cooldown) and a byte member count, each member a byte type (2) then an int64
// club id, the profile (int64 account, int64 character, name, byte gender, int job code, int job, short level, int
// map, short channel, unicode picture, three ints, int64, three ints of the achievement triple), int64 join time,
// int64 last online and a byte offline flag; 1 int64, name; 4 int64, byte; 5 int64, int; 6 int64, name; 7 and 8 an
// invite (int64, three names; 8 then an int); 9 int64, name, byte, byte; 10 int64; 15 int64, int, name; 16 int64, name,
// int; 17 int64, name, member; 18, 19 and 28 int64, name; 20 int64, name, int64; 21 int64, two names, byte; 22 int64,
// two ints; 23 int64, name, int map; 24 int64, name, profile; 26 int64, name, int64; 27 int64, two names; 29 byte, int;
// 30 int64, two names.
public class ClubModeLayoutTests {
    private static ClubMember Member() {
        var info = new PlayerInfo(new CharacterInfo(3, 4, "Mem", "m", "pic", Gender.Female, Job.Knight, 42), "", new AchievementInfo { Combat = 1, Adventure = 2, Lifestyle = 3 }, []) {
            MapId = 2000001, Channel = 2,
        };
        return new ClubMember { ClubId = 77, Info = info, JoinTime = 1000 };
    }

    private static ByteReader Open(ByteWriter writer, byte mode) {
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Club));
        Assert.That(reader.ReadByte(), Is.EqualTo(mode));
        return reader;
    }

    private static void ReadProfile(ByteReader reader) {
        Assert.That(reader.ReadLong(), Is.EqualTo(3));
        Assert.That(reader.ReadLong(), Is.EqualTo(4));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Mem"));
        Assert.That(reader.ReadByte(), Is.EqualTo((byte) Gender.Female));
        reader.ReadInt(); // job code
        Assert.That(reader.ReadInt(), Is.EqualTo((int) Job.Knight));
        Assert.That(reader.ReadShort(), Is.EqualTo(42));
        Assert.That(reader.ReadInt(), Is.EqualTo(2000001));
        Assert.That(reader.ReadShort(), Is.EqualTo(2));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("pic"));
        reader.ReadInt();
        reader.ReadInt();
        reader.ReadInt();
        reader.ReadLong();
        Assert.That((reader.ReadInt(), reader.ReadInt(), reader.ReadInt()), Is.EqualTo((1, 2, 3)));
    }

    private static void ReadMember(ByteReader reader) {
        Assert.That(reader.ReadByte(), Is.EqualTo(2));
        Assert.That(reader.ReadLong(), Is.EqualTo(77));
        ReadProfile(reader);
        Assert.That(reader.ReadLong(), Is.EqualTo(1000));
        reader.ReadLong();
        reader.ReadByte();
    }

    [TestCase(true)]
    [TestCase(false)]
    public void ClubBlockWithItsMembers(bool create) {
        ClubMember member = Member();
        var club = new Club(77, "Crew", member) { CreationTime = 5, BuffId = 8, BuffLevel = 2, NameChangeCooldown = 9, State = ClubState.Established };
        club.Members[4] = member;
        ByteReader reader = Open(create ? ClubPacket.Create(club) : ClubPacket.Update(club), (byte) (create ? 2 : 0));
        Assert.That(reader.ReadLong(), Is.EqualTo(77));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Crew"));
        Assert.That(reader.ReadLong(), Is.EqualTo(3));
        Assert.That(reader.ReadLong(), Is.EqualTo(4));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Mem"));
        Assert.That(reader.ReadLong(), Is.EqualTo(5));
        Assert.That(reader.ReadByte(), Is.EqualTo((byte) ClubState.Established));
        Assert.That(reader.ReadInt(), Is.EqualTo(8));
        Assert.That(reader.ReadInt(), Is.EqualTo(2));
        Assert.That(reader.ReadLong(), Is.EqualTo(9));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        ReadMember(reader);
        Assert.That(reader.Available, Is.Zero);
    }

    [Test]
    public void ShortModes() {
        ClubMember member = Member();
        var invite = new ClubInvite { ClubId = 77, Name = "Crew", LeaderName = "Lead", Invitee = "Inv" };

        ByteReader establish = Open(ClubPacket.Establish(new Club(77, "Crew", member)), 1);
        Assert.That(establish.ReadLong(), Is.EqualTo(77));
        Assert.That(establish.ReadUnicodeString(), Is.EqualTo("Crew"));
        Assert.That(establish.Available, Is.Zero);

        ByteReader staged = Open(ClubPacket.StagedInviteResult(77, ClubError.s_club_err_unknown), 4);
        Assert.That(staged.ReadLong(), Is.EqualTo(77));
        staged.ReadByte();
        Assert.That(staged.Available, Is.Zero);

        ByteReader deleted = Open(ClubPacket.DeleteStagedClub(77, ClubResponse.Disband), 5);
        Assert.That(deleted.ReadLong(), Is.EqualTo(77));
        Assert.That(deleted.ReadInt(), Is.EqualTo((int) ClubResponse.Disband));
        Assert.That(deleted.Available, Is.Zero);

        ByteReader invited = Open(ClubPacket.Invited(77, "Inv"), 6);
        Assert.That(invited.ReadLong(), Is.EqualTo(77));
        Assert.That(invited.ReadUnicodeString(), Is.EqualTo("Inv"));
        Assert.That(invited.Available, Is.Zero);

        foreach ((ByteWriter writer, byte mode, bool trailingInt) in new[] {
                     (ClubPacket.Invite(invite), (byte) 7, false),
                     (ClubPacket.AcceptInvite(invite), (byte) 8, true),
                 }) {
            ByteReader reader = Open(writer, mode);
            Assert.That(reader.ReadLong(), Is.EqualTo(77));
            Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Crew"));
            Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Lead"));
            Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Inv"));
            if (trailingInt) {
                reader.ReadInt();
            }
            Assert.That(reader.Available, Is.Zero);
        }

        ByteReader notification = Open(ClubPacket.InviteNotification(77, "Inv", true, 3), 9);
        Assert.That(notification.ReadLong(), Is.EqualTo(77));
        Assert.That(notification.ReadUnicodeString(), Is.EqualTo("Inv"));
        Assert.That(notification.ReadByte(), Is.EqualTo(1));
        Assert.That(notification.ReadByte(), Is.EqualTo(3));
        Assert.That(notification.Available, Is.Zero);

        ByteReader leave = Open(ClubPacket.Leave(77), 10);
        Assert.That(leave.ReadLong(), Is.EqualTo(77));
        Assert.That(leave.Available, Is.Zero);
    }

    [Test]
    public void NoticeModes() {
        ClubMember member = Member();

        ByteReader reply = Open(ClubPacket.StagedClubInviteReply(77, ClubResponse.Disband, "Nm"), 15);
        Assert.That(reply.ReadLong(), Is.EqualTo(77));
        Assert.That(reply.ReadInt(), Is.EqualTo((int) ClubResponse.Disband));
        Assert.That(reply.ReadUnicodeString(), Is.EqualTo("Nm"));
        Assert.That(reply.Available, Is.Zero);

        ByteReader disband = Open(ClubPacket.Disband(77, "Lead"), 16);
        Assert.That(disband.ReadLong(), Is.EqualTo(77));
        Assert.That(disband.ReadUnicodeString(), Is.EqualTo("Lead"));
        Assert.That(disband.ReadInt(), Is.EqualTo((int) ClubResponse.Disband));
        Assert.That(disband.Available, Is.Zero);

        ByteReader accepted = Open(ClubPacket.NotifyAcceptInvite(member, "Lead"), 17);
        Assert.That(accepted.ReadLong(), Is.EqualTo(77));
        Assert.That(accepted.ReadUnicodeString(), Is.EqualTo("Lead"));
        ReadMember(accepted);
        Assert.That(accepted.Available, Is.Zero);

        foreach ((ByteWriter writer, byte mode) in new[] {
                     (ClubPacket.LeaveNotice(77, "Nm"), (byte) 18),
                     (ClubPacket.NotifyLogin(77, "Nm"), (byte) 19),
                     (ClubPacket.DeletedMember(77, "Nm"), (byte) 28),
                 }) {
            ByteReader reader = Open(writer, mode);
            Assert.That(reader.ReadLong(), Is.EqualTo(77));
            Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Nm"));
            Assert.That(reader.Available, Is.Zero);
        }

        ByteReader logout = Open(ClubPacket.NotifyLogout(77, "Nm", 123), 20);
        Assert.That(logout.ReadLong(), Is.EqualTo(77));
        Assert.That(logout.ReadUnicodeString(), Is.EqualTo("Nm"));
        Assert.That(logout.ReadLong(), Is.EqualTo(123));
        Assert.That(logout.Available, Is.Zero);

        ByteReader leader = Open(ClubPacket.UpdateLeader(77, "Old", "New"), 21);
        Assert.That(leader.ReadLong(), Is.EqualTo(77));
        Assert.That(leader.ReadUnicodeString(), Is.EqualTo("Old"));
        Assert.That(leader.ReadUnicodeString(), Is.EqualTo("New"));
        Assert.That(leader.ReadByte(), Is.EqualTo(1));
        Assert.That(leader.Available, Is.Zero);
    }

    [Test]
    public void BuffMapMemberRenameErrorAndJoinModes() {
        ClubMember member = Member();

        ByteReader buff = Open(ClubPacket.ChangeBuff(77, 8, 2), 22);
        Assert.That((buff.ReadLong(), buff.ReadInt(), buff.ReadInt()), Is.EqualTo((77L, 8, 2)));
        Assert.That(buff.Available, Is.Zero);

        ByteReader map = Open(ClubPacket.UpdateMemberMap(77, "Nm", 2000001), 23);
        Assert.That(map.ReadLong(), Is.EqualTo(77));
        Assert.That(map.ReadUnicodeString(), Is.EqualTo("Nm"));
        Assert.That(map.ReadInt(), Is.EqualTo(2000001));
        Assert.That(map.Available, Is.Zero);

        ByteReader update = Open(ClubPacket.UpdateMember(member), 24);
        Assert.That(update.ReadLong(), Is.EqualTo(77));
        Assert.That(update.ReadUnicodeString(), Is.EqualTo("Mem"));
        ReadProfile(update);
        Assert.That(update.Available, Is.Zero);

        ByteReader rename = Open(ClubPacket.Rename(77, "New", 456), 26);
        Assert.That(rename.ReadLong(), Is.EqualTo(77));
        Assert.That(rename.ReadUnicodeString(), Is.EqualTo("New"));
        Assert.That(rename.ReadLong(), Is.EqualTo(456));
        Assert.That(rename.Available, Is.Zero);

        ByteReader memberName = Open(ClubPacket.UpdateMemberName(77, "Old", "New"), 27);
        Assert.That(memberName.ReadLong(), Is.EqualTo(77));
        Assert.That(memberName.ReadUnicodeString(), Is.EqualTo("Old"));
        Assert.That(memberName.ReadUnicodeString(), Is.EqualTo("New"));
        Assert.That(memberName.Available, Is.Zero);

        ByteReader error = Open(ClubPacket.Error(ClubError.s_club_err_unknown), 29);
        Assert.That(error.ReadByte(), Is.EqualTo(1));
        Assert.That(error.ReadInt(), Is.EqualTo((int) ClubError.s_club_err_unknown));
        Assert.That(error.Available, Is.Zero);

        ByteReader join = Open(ClubPacket.Join(member, "Crew"), 30);
        Assert.That(join.ReadLong(), Is.EqualTo(77));
        Assert.That(join.ReadUnicodeString(), Is.EqualTo("Mem"));
        Assert.That(join.ReadUnicodeString(), Is.EqualTo("Crew"));
        Assert.That(join.Available, Is.Zero);
    }
}
