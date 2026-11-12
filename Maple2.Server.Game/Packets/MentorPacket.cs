using Maple2.Database.Extensions;
using Maple2.Model.Enum;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Model;

namespace Maple2.Server.Game.Packets;

public static class MentorPacket {
    private enum Command : byte {
        UpdateRole = 1,
        MenteeInvitations = 2,
        MyList = 3,
        AssignReturningUser = 4,
        SendMenteeInvite = 5,
        AcceptMentorInvite = 6,
        DeclineMentorInvite = 7,
        AssignMentor = 8,
        Load = 9,
        LoginPoints = 10,
        DailyPoints = 11,
        Unknown12 = 12,
        ReceiveMenteeInvite = 14,
        Unknown15 = 15,
        Unknown16 = 16,
    }

    public static ByteWriter UpdateRole(MentorRole role, int objectId) {
        var pWriter = Packet.Of(SendOp.Mentor);
        pWriter.Write<Command>(Command.UpdateRole);
        pWriter.Write<MentorRole>(role);
        pWriter.WriteInt(objectId);

        return pWriter;
    }

    // Client: CMentoringManager::OnMentorList (mentoringmanager.cpp:320) and OnMenteeList (:333) read an int count and
    // per entry CMentor::Read (commonfiles/mentor.cpp:230): long, long, long, text name, short level, text, int,
    // bool online, int. The server keeps no mentoring pairs yet, so both lists are sent empty.
    public static ByteWriter MenteeInvitations() {
        var pWriter = Packet.Of(SendOp.Mentor);
        pWriter.Write<Command>(Command.MenteeInvitations);
        pWriter.WriteInt(0);

        return pWriter;
    }

    /// <summary>
    /// If player is a mentee, the list is of mentors. If player is a mentor, the list is of mentees.
    /// </summary>
    public static ByteWriter MyList() {
        var pWriter = Packet.Of(SendOp.Mentor);
        pWriter.Write<Command>(Command.MyList);
        pWriter.WriteInt(0);

        return pWriter;
    }

    public static ByteWriter AssignReturningUser() {
        var pWriter = Packet.Of(SendOp.Mentor);
        pWriter.Write<Command>(Command.AssignReturningUser);

        return pWriter;
    }

    public static ByteWriter MenteeInvite(long characterId, string name) {
        var pWriter = Packet.Of(SendOp.Mentor);
        pWriter.Write<Command>(Command.SendMenteeInvite);
        pWriter.WriteLong(characterId);
        pWriter.WriteUnicodeString(name);

        return pWriter;
    }

    // s_mentoring_invite_approve_mentor
    public static ByteWriter AcceptMentorInvite() {
        var pWriter = Packet.Of(SendOp.Mentor);
        pWriter.Write<Command>(Command.AcceptMentorInvite);
        pWriter.WriteLong();

        return pWriter;
    }

    // s_mentoring_invite_decline_mentor
    public static ByteWriter DeclineMentorInvite() {
        var pWriter = Packet.Of(SendOp.Mentor);
        pWriter.Write<Command>(Command.DeclineMentorInvite);
        pWriter.WriteLong();

        return pWriter;
    }

    public static ByteWriter AssignMentor() {
        var pWriter = Packet.Of(SendOp.Mentor);
        pWriter.Write<Command>(Command.AssignMentor);

        return pWriter;
    }

    public static ByteWriter Load() {
        var pWriter = Packet.Of(SendOp.Mentor);
        pWriter.Write<Command>(Command.Load);

        return pWriter;
    }

    public static ByteWriter LoginPoints() {
        var pWriter = Packet.Of(SendOp.Mentor);
        pWriter.Write<Command>(Command.LoginPoints);
        pWriter.WriteInt(100); // points received
        pWriter.WriteLong(DateTime.Now.AddDays(-0).ToEpochSeconds());

        return pWriter;
    }

    public static ByteWriter DailyPoints() {
        var pWriter = Packet.Of(SendOp.Mentor);
        pWriter.Write<Command>(Command.DailyPoints);
        pWriter.WriteInt(1000);
        pWriter.WriteInt(0); // mentee points received today
        pWriter.WriteLong(DateTime.Now.AddDays(+2).ToEpochSeconds()); // reset timestamp

        return pWriter;
    }

    public static ByteWriter Unknown12() {
        var pWriter = Packet.Of(SendOp.Mentor);
        pWriter.Write<Command>(Command.Unknown12);
        pWriter.WriteLong();
        pWriter.WriteLong();

        return pWriter;
    }

    public static ByteWriter ReceiveMenteeInvite() {
        var pWriter = Packet.Of(SendOp.Mentor);
        pWriter.Write<Command>(Command.ReceiveMenteeInvite);
        pWriter.WriteUnicodeString(); // Inviter name
        pWriter.WriteLong();
        pWriter.WriteLong();
        pWriter.WriteLong();
        pWriter.WriteUnicodeString();
        pWriter.WriteShort();
        pWriter.WriteUnicodeString();
        pWriter.WriteInt();
        pWriter.WriteByte();
        pWriter.WriteInt();


        return pWriter;
    }

    public static ByteWriter Unknown15(int count) {
        var pWriter = Packet.Of(SendOp.Mentor);
        pWriter.Write<Command>(Command.Unknown15);

        pWriter.WriteInt(count);
        for (int i = 0; i < count; i++) {
            pWriter.WriteLong(50000);
            pWriter.WriteByte(1);
        }

        return pWriter;
    }

    // CMentoringManager::OnError (mentoringmanager.cpp:500): a byte result, which picks the notice, and an int.
    public static ByteWriter Error(MentorError error) {
        var pWriter = Packet.Of(SendOp.Mentor);
        pWriter.Write<Command>(Command.Unknown16);
        pWriter.WriteByte((byte) error);
        pWriter.WriteInt(0);

        return pWriter;
    }
}
