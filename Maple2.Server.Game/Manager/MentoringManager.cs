using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Serilog;

namespace Maple2.Server.Game.Manager;

public class MentoringManager : IDisposable {
    private readonly GameSession session;
    public MentorRole Role => session.Player.Value.Character.MentorRole;


    private readonly ILogger logger = Log.Logger.ForContext<MentoringManager>();

    public MentoringManager(GameSession session) {
        this.session = session;
    }

    public void Dispose() {
        session.Dispose();
    }

    // The register button of the mentee page (Mentor 4, role Mentee) and of the mentor page (Mentor 8, role Mentor).
    // The client answers Mentor 1 by setting the role on the player with that object id, for every client that
    // shows the player, and Mentor 4 or 8 by closing the dialog with a notice.
    public void UpdateRole(MentorRole role) {
        if (role is not (MentorRole.Mentee or MentorRole.Mentor)) {
            return;
        }
        GameEvent? gameEvent = session.FindEvent(GameEventType.ReturnUser).FirstOrDefault();
        if (gameEvent?.Metadata.Data is not ReturnUser returnUser) {
            // No season runs; the client's menu does not open the dialog then.
            if (role == MentorRole.Mentee) {
                session.Send(MentorPacket.Error(MentorError.IsNotReturnUser));
            }
            return;
        }
        if (Role is MentorRole.Mentee or MentorRole.Mentor) {
            return;
        }

        if (role == MentorRole.Mentee) {
            foreach (int questId in returnUser.QuestIds) {
                if (session.Quest.Start(questId) == QuestError.s_quest_error_inventory_full) {
                    session.Send(QuestPacket.Error(QuestError.s_quest_error_inventory_full));
                }
            }
        }

        session.Player.Value.Character.MentorRole = role;
        session.PlayerInfo.SendUpdate(new PlayerUpdateRequest {
            AccountId = session.AccountId,
            CharacterId = session.CharacterId,
            MentorRole = (int) role,
            Async = true,
        });
        ByteWriter update = MentorPacket.UpdateRole(role, session.Player.ObjectId);
        if (session.Field is null) {
            session.Send(update);
        } else {
            session.Field.Broadcast(update);
        }
        session.Send(role == MentorRole.Mentee ? MentorPacket.AssignReturningUser() : MentorPacket.AssignMentor());
    }
}
