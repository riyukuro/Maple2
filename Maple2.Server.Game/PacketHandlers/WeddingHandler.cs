using Maple2.Database.Extensions;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Game.PacketHandlers;

public class WeddingHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.Wedding;

    private enum Command : byte {
        Propose = 3,
        ProposalReply = 4,
        CancelProposal = 5,
        DivorceVisit = 7,
        DivorceReply = 9,
        ReserveHall = 13,
        ReserveHallReply = 14,
        CancelReservation = 16,
        CancelNoRefundReply = 17,
        CancelReservationReply = 18,
        ChangeReservation = 20,
        ChangeReservationReply = 21,
        EnterCeremony = 26,
        InvitationFindUser = 27,
        SendInvitation = 28,
        MutualAgreeAnswer = 30,
        SetCoupleMessage = 34,
        ReceiveSkillItem = 37,
    }

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();

        switch (command) {
            case Command.Propose:
                HandlePropose(session, packet);
                return;
            case Command.ProposalReply:
                HandleProposalReply(session, packet);
                return;
            case Command.CancelProposal:
                HandleCancelProposal(session, packet);
                return;
            case Command.ReserveHall:
                HandleReserveHall(session, packet);
                return;
            case Command.ReserveHallReply:
                HandleReserveHallReply(session, packet);
                return;
            case Command.CancelReservation:
                HandleCancelReservation(session);
                return;
            case Command.CancelNoRefundReply:
                CancelNoRefundReply(session, packet);
                return;
            case Command.CancelReservationReply:
                HandleCancelReservationReply(session, packet);
                return;
            case Command.ChangeReservation:
                HandleChangeReservation(session, packet);
                return;
            case Command.ChangeReservationReply:
                HandleChangeReservationReply(session, packet);
                return;
            case Command.EnterCeremony:
                HandleEnterCeremony(session, packet);
                return;
            case Command.MutualAgreeAnswer:
                HandleMutualAgreeAnswer(session, packet);
                return;
            case Command.InvitationFindUser:
                HandleInvitationFindUser(session, packet);
                return;
            case Command.SendInvitation:
                HandleSendInvitation(session, packet);
                return;
            case Command.DivorceReply:
                // WeddingManager::OnDivorceAnswer (weddingmanager.cpp:478): the answer to the box of Wedding 8, one
                // short (13 = yes, 14 = no).
                session.Marriage.DivorceAnswer(packet.ReadShort());
                return;
            case Command.DivorceVisit:
                // UIWeddingDialog::SendWedding (uiweddingdialog.cpp:278): the yes of the box the wedding window
                // shows the partner who did not file a divorce (OnDivorce, :326). Nothing after the mode.
                session.Marriage.DivorceVisit();
                return;
            case Command.SetCoupleMessage:
                session.Marriage.SetCoupleMessage(packet.ReadUnicodeString());
                return;
            case Command.ReceiveSkillItem:
                // UIWeddingPageSkill::vfunc_3 (uiweddingpage.cpp:310): the id of a wedding skill of type 1 whose
                // item the character does not hold. The skills and their items are in the client's wedding table,
                // which the server's data does not have; the result texts have none for this. Read and logged.
                Logger.Debug("Wedding {Command} from {CharacterId}: skill {Id}", command, session.CharacterId, packet.ReadInt());
                return;
        }
    }

    private static void HandlePropose(GameSession session, IByteReader packet) {
        if (session.Field is null) return;
        string otherPlayerName = packet.ReadUnicodeString();

        if (!session.Field.TryGetPlayer(otherPlayerName, out FieldPlayer? otherPlayer)) {
            session.Send(WeddingPacket.Error(WeddingError.s_wedding_result_err_same_field));
            return;
        }

        WeddingError error = session.Marriage.Propose(otherPlayer);

        if (error == WeddingError.s_wedding_msg_box_propose_to && !session.Marriage.BeginProposal(otherPlayer)) {
            error = WeddingError.s_wedding_result_err_already_propose_request;
        }

        session.Send(WeddingPacket.Error(error));
        if (error == WeddingError.s_wedding_msg_box_propose_to) {
            otherPlayer.Session.Send(WeddingPacket.Proposal(session.PlayerName, session.CharacterId));
            session.ConditionUpdate(ConditionType.wedding_propose);
        }
    }

    private static void HandleProposalReply(GameSession session, IByteReader packet) {
        if (session.Field is null) return;
        // WeddingManager::OnProposeAnswer: 1 accept, 2 decline, 6 the answer box timed out.
        var response = (WeddingError) packet.ReadShort();
        long playerId = packet.ReadLong(); // the proposer

        if (response is not (WeddingError.s_wedding_propose_ok or WeddingError.s_wedding_result_decline_propose
                or WeddingError.s_wedding_result_cancel_propose_expire)) {
            return;
        }
        // Only an answer to the proposal this player is waiting on, from the player who sent it.
        if (!session.Field.TryGetPlayerById(playerId, out FieldPlayer? otherPlayer) ||
            !otherPlayer.Session.Marriage.EndProposalTo(session.Player)) {
            return;
        }

        if (response != WeddingError.s_wedding_propose_ok) {
            if (response == WeddingError.s_wedding_result_decline_propose) {
                session.ConditionUpdate(ConditionType.wedding_propose_decline);
                otherPlayer.Session.ConditionUpdate(ConditionType.wedding_propose_declined);
            }
            otherPlayer.Session.Send(WeddingPacket.Error(response));
            return;
        }

        WeddingError error = session.Marriage.AcceptProposal(otherPlayer);
        if (error != WeddingError.none) {
            session.Send(WeddingPacket.Error(error));
            otherPlayer.Session.Send(WeddingPacket.Error(WeddingError.s_wedding_result_decline_propose));
        }
    }

    // The proposer closes the "proposed to" box before the answer (WeddingManager::OnProposeSentAnswer).
    private static void HandleCancelProposal(GameSession session, IByteReader packet) {
        if (session.Field is null) return;
        long playerId = packet.ReadLong(); // character id of the player who got the proposal

        // Only the proposer of a waiting proposal can withdraw it, and only from its target.
        if (!session.Field.TryGetPlayerById(playerId, out FieldPlayer? otherPlayer) ||
            !session.Marriage.EndProposalTo(otherPlayer)) {
            return;
        }

        session.Send(WeddingPacket.Error(WeddingError.s_wedding_result_cancel_propose_to));
        // Result 5 shows the withdrawn notice and steps the message box queue (WeddingManager::OnResult).
        otherPlayer.Session.Send(WeddingPacket.Error(WeddingError.s_wedding_result_cancel_propose_from));
    }

    private static void HandleReserveHall(GameSession session, IByteReader packet) {
        if (session.Marriage.Marriage.Status != MaritalStatus.Engaged) {
            session.Send(WeddingPacket.Error(WeddingError.s_wedding_result_err_only_promise_user));
            return;
        }

        if (session.Marriage.WeddingHall.Id != 0) {
            session.Send(WeddingPacket.Error(WeddingError.s_wedding_result_already_reservation));
            return;
        }

        var hall = packet.ReadClass<WeddingHall>();
        bool useCoupon = packet.ReadBool();

        WeddingError error = session.Marriage.ReserveHall(hall, useCoupon);
        session.Send(WeddingPacket.Error(error));
    }

    private static void HandleReserveHallReply(GameSession session, IByteReader packet) {
        var response = packet.Read<WeddingError>();

        if (response != WeddingError.confirm_reservation) {
            session.Send(WeddingPacket.Error(response));
            return;
        }

        WeddingError error = session.Marriage.ConfirmReservation();
        if (error != WeddingError.s_wedding_propose_ok) {
            session.Send(WeddingPacket.Error(error));
        }
    }

    private static void HandleCancelReservation(GameSession session) {
        if (session.Marriage.WeddingHall.Id == 0) {
            session.Send(WeddingPacket.Error(WeddingError.s_wedding_result_not_find_weddinghall));
            return;
        }

        session.Marriage.CancelReservationRequest();
    }

    private static void CancelNoRefundReply(GameSession session, IByteReader packet) {
        var response = packet.Read<WeddingError>();

        if (response == WeddingError.decline_reservation_cancel) {
            session.Send(WeddingPacket.DeclineCancelReservation());
            return;
        }

        WeddingError error = session.Marriage.CancelNoRefund();
        if (error != WeddingError.none) {
            session.Send(WeddingPacket.Error(error));
        }
    }

    private static void HandleCancelReservationReply(GameSession session, IByteReader packet) {
        var response = packet.Read<WeddingError>();

        WeddingError error = session.Marriage.CancelReservationReply(response);
        if (error != WeddingError.none) {
            session.Send(WeddingPacket.Error(error));
        }
    }

    private static void HandleChangeReservation(GameSession session, IByteReader packet) {
        var hall = packet.ReadClass<WeddingHall>();

        WeddingError error = session.Marriage.ChangeReservationRequest(hall);
        if (error != WeddingError.none) {
            session.Send(WeddingPacket.Error(error));
        }
    }

    private static void HandleChangeReservationReply(GameSession session, IByteReader packet) {
        var response = packet.Read<WeddingError>();

        WeddingError error = session.Marriage.ChangeReservationReply(response);
        if (error != WeddingError.none) {
            session.Send(WeddingPacket.Error(error));
        }
    }

    // Client: WeddingManager::SendInvitation (weddingmanager.cpp:386) sends the id of a ticket it holds. The
    // ticket is offered while its time is at most 1800 s away (CollectTickets, :906), so the same window is the
    // server's rule. Each marriage has its own room of the hall map.
    private static void HandleEnterCeremony(GameSession session, IByteReader packet) {
        long marriageId = packet.ReadLong();
        if (session.Field == null) {
            return;
        }

        WeddingHall? hall;
        Marriage? marriage;
        using (GameStorage.Request db = session.GameStorage.Context()) {
            hall = db.GetWeddingHall(marriageId: marriageId);
            marriage = hall == null ? null : db.GetMarriage(weddingId: marriageId);
        }
        if (hall == null || marriage == null) {
            session.Send(WeddingPacket.Error(WeddingError.s_wedding_result_err_invalid_wedding_hall));
            return;
        }

        if (!WeddingCeremony.InEnterWindow(hall.CeremonyTime, DateTimeOffset.UtcNow.ToUnixTimeSeconds())) {
            session.Send(WeddingPacket.Error(WeddingError.s_wedding_visit_failed_invalid_time));
            return;
        }

        var ceremony = new WeddingCeremony(marriage.Id, marriage.Partner1.CharacterId, marriage.Partner2.CharacterId);
        if (!WeddingCeremony.CanEnter(ceremony.IsCouple(session.CharacterId), hall.Public, hall.GuestList.ContainsKey(session.CharacterId))) {
            session.Send(WeddingPacket.Error(WeddingError.s_wedding_visit_failed_common));
            return;
        }

        if (!session.TableMetadata.WeddingTable.Packages.TryGetValue(hall.PackageId, out WeddingPackage? package)
            || !package.Halls.TryGetValue(hall.PackageHallId, out WeddingPackage.HallData? hallData)) {
            session.Send(WeddingPacket.Error(WeddingError.s_wedding_result_err_invalid_wedding_hall));
            return;
        }

        // The hour of the ceremony, not of the entry, picks the day or the night map, so that everyone who enters
        // in the window meets in one room.
        int mapId = WeddingCeremony.HallMap(hallData.MapId, hallData.NightMapId, hall.CeremonyTime.FromEpochSeconds().Hour);
        ceremony.Gifts = WeddingCeremony.CompleteGifts(hallData.CompleteItems, night: mapId != hallData.MapId);
        if (session.Field.MapId == mapId && session.Field.Wedding?.MarriageId == marriageId) {
            session.Send(WeddingPacket.Error(WeddingError.s_wedding_visit_failed_same_place));
            return;
        }

        // From a dungeon room the field change is a migration that is already under way when PrepareField
        // returns, and it would arrive in a room that is not the couple's.
        if (!WeddingCeremony.CanEnterFrom(session.Field is DungeonFieldManager)) {
            session.Send(WeddingPacket.Error(WeddingError.s_wedding_visit_failed_common));
            return;
        }

        if (!session.PrepareField(mapId, out FieldManager? field, ownerId: marriageId)) {
            session.Send(FieldEnterPacket.Error(MigrationError.s_move_err_default));
            return;
        }

        Interlocked.CompareExchange(ref field.Wedding, ceremony, null);
        session.Send(FieldEnterPacket.Request(session.Player));
    }

    // Client: WeddingManager::OnActingAnswer (weddingmanager.cpp:821) sends the type of the box and the answer;
    // the typed-name steps end with type 3 and true (OnLastStepAnswer, :809). Only an answer of one of the couple
    // to a question the hall trigger has open is kept.
    private static void HandleMutualAgreeAnswer(GameSession session, IByteReader packet) {
        var type = (WeddingAgreeType) packet.ReadByte();
        bool accept = packet.ReadBool();

        session.Field?.Wedding?.Answer(session.CharacterId, type, accept);
    }

    // Client: the stranger page of the guest dialog sends the typed name (uiweddingguestdialog.cpp:688). The
    // answer carries character id 0 when there is no such character.
    private static void HandleInvitationFindUser(GameSession session, IByteReader packet) {
        string inviteeName = packet.ReadUnicodeString();
        if (session.Marriage.WeddingHall.Id == 0) {
            session.Send(WeddingPacket.Error(WeddingError.s_wedding_result_not_find_weddinghall));
            return;
        }

        (long AccountId, long CharacterId, string Name)? found;
        using (GameStorage.Request db = session.GameStorage.Context()) {
            found = string.IsNullOrWhiteSpace(inviteeName) ? null : db.FindCharacterByName(inviteeName);
        }
        session.Send(found == null
            ? WeddingPacket.InvitationFound(0, 0, inviteeName)
            : WeddingPacket.InvitationFound(found.Value.AccountId, found.Value.CharacterId, found.Value.Name));
    }

    // Client: UIWeddingInvitationDialog::SendInvitation (uiweddinginvitationdialog.cpp:223) sends a count and
    // that many guests (WeddingGuest::Write, weddingproxy.cpp:456): a short for the page the guest was picked
    // on (0 guild, 1 buddy, 2 found by name), the account id, the character id and the name. Only the character
    // id is taken; the account is looked up.
    private static void HandleSendInvitation(GameSession session, IByteReader packet) {
        int inviteCount = packet.ReadInt();
        if (inviteCount < 0 || inviteCount > session.ServerTableMetadata.ConstantsTable.WeddingInvitationMaxCount) {
            session.Send(WeddingPacket.Error(WeddingError.s_wedding_result_wedding_invitation_count));
            return;
        }

        var characterIds = new List<long>();
        for (int i = 0; i < inviteCount; i++) {
            packet.ReadShort();
            packet.ReadLong();
            characterIds.Add(packet.ReadLong());
            packet.ReadUnicodeString();
        }

        WeddingError error = session.Marriage.SendInvitations(WeddingInviteRules.Guests(characterIds));
        if (error != WeddingError.none) {
            session.Send(WeddingPacket.Error(error));
        }
    }
}
