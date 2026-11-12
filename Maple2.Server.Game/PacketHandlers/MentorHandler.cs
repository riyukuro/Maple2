using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
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
            case Command.DeclineMentor:
                HandleAnswer(session, packet);
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
        session.Send(MentorPacket.MenteeInvitations());
    }

    // UIInviteMentoringDialog::InviteMentor (uiinvitementoringdialog.cpp:408): a mentor invites a mentee, picked from
    // the candidate list (its id and name) or typed (id 0 and the name). The name is always there, so it is the key.
    private void HandleInvite(GameSession session, IByteReader packet) {
        packet.ReadLong();
        string name = packet.ReadUnicodeString();
        if (session.Mentoring.Role != MentorRole.Mentor) {
            return;
        }

        long targetId;
        using (GameStorage.Request db = session.GameStorage.Context()) {
            targetId = db.GetCharacterId(name);
        }
        if (targetId == 0 || !session.PlayerInfo.GetOrFetch(targetId, out PlayerInfo? target)) {
            session.Send(MentorPacket.Error(MentorError.TargetNotExists));
            return;
        }
        if (targetId == session.CharacterId || target.AccountId == session.AccountId) {
            session.Send(MentorPacket.Error(MentorError.InvalidTarget));
            return;
        }
        if (target.MentorRole != MentorRole.Mentee) {
            session.Send(MentorPacket.Error(MentorError.TargetIsNotMentee));
            return;
        }
        // The server keeps no mentoring requests or pairs: an invite that passes is not delivered.
    }

    // The mentee's answer to a mentor's request, the request's id (uiinvitementoringdialog.cpp:266 and :453). The
    // server keeps no requests, so the id names none.
    private void HandleAnswer(GameSession session, IByteReader packet) {
        packet.ReadLong();
        session.Send(MentorPacket.Error(MentorError.TargetNotExists));
    }

    private void HandleLoad(GameSession session) {
        session.Send(MentorPacket.MenteeInvitations());
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
