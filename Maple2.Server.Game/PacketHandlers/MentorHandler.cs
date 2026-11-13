using Maple2.Model.Enum;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers;

public class MentorHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.Mentor;

    private enum Command : byte {
        Reward = 0,
        MentorList = 2,
        AssignMentee = 4,
        Invite = 5, // by name
        AcceptMentor = 6,
        DeclineMentor = 7,
        AssignMentor = 8,
        Load = 9,
    }

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        switch (command) {
            case Command.Reward:
                HandleReward(session);
                break;
            case Command.AssignMentee:
                HandleAssignMentee(session);
                break;
            case Command.Load:
                HandleLoad(session);
                break;
            case Command.MentorList:
                HandleMentorList(session, packet);
                break;
            case Command.AssignMentor:
                session.Mentoring.UpdateRole(MentorRole.Mentor);
                break;
            case Command.Invite:
                HandleInvite(session, packet);
                break;
            case Command.AcceptMentor:
                HandleAccept(session, packet);
                break;
            case Command.DeclineMentor:
                HandleDecline(session, packet);
                break;
        }
    }

    private void HandleReward(GameSession session) {
        session.Send(MentorPacket.Unknown12());
    }

    private void HandleAssignMentee(GameSession session) {
        session.Mentoring.UpdateRole(MentorRole.Mentee);
    }

    private void HandleMentorList(GameSession session, IByteReader packet) {
        int unknown = packet.ReadInt(); // 0
        session.Mentoring.SendRequests();
    }

    // UIInviteMentoringDialog::InviteMentor (uiinvitementoringdialog.cpp:408): a mentor invites a mentee, picked from
    // the candidate list (its id and name) or typed (id 0 and the name). The name is always there, so it is the key.
    private void HandleInvite(GameSession session, IByteReader packet) {
        packet.ReadLong();
        string name = packet.ReadUnicodeString();
        session.Mentoring.Invite(name);
    }

    // The mentee's answer to a mentor's request, the request's id (uiinvitementoringdialog.cpp:266 and :453).
    private void HandleAccept(GameSession session, IByteReader packet) {
        session.Mentoring.Accept(packet.ReadLong());
    }

    private void HandleDecline(GameSession session, IByteReader packet) {
        session.Mentoring.Decline(packet.ReadLong());
    }

    private void HandleLoad(GameSession session) {
        session.Mentoring.SendRequests();
        session.Send(MentorPacket.Load());
        //session.Send(MentorPacket.MyList());
        //  session.Send(MentorPacket.LoginPoints());
        //  session.Send(MentorPacket.DailyPoints());
        //  session.Send(MentorPacket.Unknown12());
        //   session.Send(MentorPacket.Unknown15(10));
        //   session.Send(MentorPacket.MenteeInvitations());
        //  session.Send(MentorPacket.Unknown16());
        //session.Send(MentorPacket.UpdateRole(session.Player.ObjectId));
        //session.Send(MentorPacket.MenteeList());
    }
}
