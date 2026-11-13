using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.World.Service;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Serilog;

namespace Maple2.Server.Game.Manager;

public class MentoringManager : IDisposable {
    private readonly GameSession session;
    public MentorRole Role => session.Player.Value.Character.MentorRole;


    private readonly ILogger logger = Log.Logger.ForContext<MentoringManager>();

    // The requests and pairs of this game server are checked and stored one at a time; the unique (mentor, mentee)
    // index keeps a second row out when two game servers store the same request.
    private static readonly object StoreLock = new();

    // The running ReturnUser season. Without one the client's menu does not open the mentoring dialogs, and no
    // request or pair is stored or sent.
    private ReturnUser? Season => session.FindEvent(GameEventType.ReturnUser).FirstOrDefault()?.Metadata.Data as ReturnUser;

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
            // A second press of the own page's button is dropped. The other page's button gets the client's
            // "invalid target" notice (chosen here: the client has no notice of its own for it).
            if (Role != role) {
                session.Send(MentorPacket.Error(MentorError.InvalidTarget));
            }
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

    // The login: the waiting requests to a mentee (Mentor 2), the pairs (Mentor 3) and whether their other
    // characters are online (Mentor 15). Without a season only the empty pair list, as before.
    public void Load() {
        if (Season == null) {
            session.Send(MentorPacket.MyList());
            return;
        }

        (MentorPacket.Entry[] requests, MentorPacket.Entry[] pairs) = Lists();
        session.Send(MentorPacket.MenteeInvitations(requests));
        session.Send(MentorPacket.MyList(pairs));
        session.Send(MentorPacket.PairOnline(pairs));
    }

    // The mentee page's list request (Mentor 2).
    public void SendRequests() {
        session.Send(Season == null ? MentorPacket.MenteeInvitations() : MentorPacket.MenteeInvitations(Lists().Requests));
    }

    // UIInviteMentoringDialog::InviteMentor (uiinvitementoringdialog.cpp:408): a mentor invites a mentee by name. The
    // mentor gets Mentor 5 with the mentee's name; the mentee, when online, Mentor 14 with the request.
    public void Invite(string name) {
        if (Role != MentorRole.Mentor) {
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
        if (Season == null) {
            return;
        }

        // The limits are the server table's (constants.xml); the client does not read them.
        ConstantsTable constants = session.ServerTableMetadata.ConstantsTable;
        MentorError? error;
        MentoringEntry? request;
        lock (StoreLock) {
            using GameStorage.Request db = session.GameStorage.Context();
            error = db.AddMentoringRequest(session.CharacterId, targetId, Constant.MaxMentees, constants.MentorMaxWaitingCount,
                constants.MenteeMaxReceivedCount, out request);
        }
        if (error != null || request == null) {
            session.Send(MentorPacket.Error(error ?? MentorError.TargetNotExists));
            return;
        }

        session.Send(MentorPacket.MenteeInvite(targetId, target.Name));
        try {
            session.World.Mentoring(new MentoringRequest {
                ReceiverId = targetId,
                Requested = new MentoringRequest.Types.Requested { RequestId = request.Id },
            });
        } catch (Exception ex) {
            logger.Warning(ex, "Mentoring request {Id} not delivered", request.Id);
        }
    }

    // A mentor's request stored for this player (from the world): shown when it still waits for this player.
    public void ReceiveRequest(long id) {
        if (Season == null) {
            return;
        }

        MentoringEntry? row;
        using (GameStorage.Request db = session.GameStorage.Context()) {
            row = db.GetMentoringEntry(id);
        }
        if (row is not { Accepted: false } || row.MenteeId != session.CharacterId || ToEntry(row) is not { } request) {
            return;
        }
        session.Send(MentorPacket.ReceiveMenteeInvite(request));
    }

    // The mentee accepts a mentor's request (uiinvitementoringdialog.cpp:453): only a waiting request to this player.
    // The mentee gets Mentor 6 (the client moves the request to its pair and drops the others, which are removed
    // here too); the mentor, when online, its pair list again.
    public void Accept(long id) {
        if (Season == null) {
            session.Send(MentorPacket.Error(MentorError.TargetNotExists));
            return;
        }

        MentorError? error;
        MentoringEntry? pair;
        lock (StoreLock) {
            using GameStorage.Request db = session.GameStorage.Context();
            error = db.AcceptMentoring(id, session.CharacterId, Constant.MaxMentees, out pair);
        }
        if (error != null || pair == null) {
            session.Send(MentorPacket.Error(error ?? MentorError.TargetNotExists));
            if (error == MentorError.InvalidTarget) {
                // The mentor has all its mentees: the request was removed, the list shows the ones left.
                session.Send(MentorPacket.MenteeInvitations(Lists().Requests));
            }
            return;
        }

        session.Send(MentorPacket.AcceptMentorInvite(id));
        try {
            session.World.Mentoring(new MentoringRequest {
                ReceiverId = pair.MentorId,
                Reload = new MentoringRequest.Types.Reload(),
            });
        } catch (Exception ex) {
            logger.Warning(ex, "Mentoring pair {Id} not sent to the mentor", pair.Id);
        }
    }

    // The mentee declines a mentor's request (uiinvitementoringdialog.cpp:266): only a waiting request to this player.
    public void Decline(long id) {
        bool removed = false;
        if (Season != null) {
            lock (StoreLock) {
                using GameStorage.Request db = session.GameStorage.Context();
                removed = db.DeclineMentoring(id, session.CharacterId);
            }
        }
        session.Send(removed ? MentorPacket.DeclineMentorInvite(id) : MentorPacket.Error(MentorError.TargetNotExists));
    }

    // The stored pairs changed (from the world): the pair list again.
    public void Reload() {
        if (Season == null) {
            return;
        }

        MentorPacket.Entry[] pairs = Lists().Pairs;
        session.Send(MentorPacket.MyList(pairs));
        session.Send(MentorPacket.PairOnline(pairs));
    }

    // The waiting requests to this player (as mentee) and its pairs, each with the other character.
    private (MentorPacket.Entry[] Requests, MentorPacket.Entry[] Pairs) Lists() {
        IList<MentoringEntry> rows;
        using (GameStorage.Request db = session.GameStorage.Context()) {
            rows = db.GetMentoring(session.CharacterId);
        }

        MentorPacket.Entry[] requests = rows.Where(row => !row.Accepted && row.MenteeId == session.CharacterId)
            .Select(ToEntry).OfType<MentorPacket.Entry>().ToArray();
        MentorPacket.Entry[] pairs = rows.Where(row => row.Accepted).Select(ToEntry).OfType<MentorPacket.Entry>().ToArray();
        return (requests, pairs);
    }

    private MentorPacket.Entry? ToEntry(MentoringEntry row) {
        return session.PlayerInfo.GetOrFetch(row.PartnerOf(session.CharacterId), out PlayerInfo? partner)
            ? new MentorPacket.Entry(row.Id, partner)
            : null;
    }
}
