using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Model;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Game.Packets;

public static class WeddingPacket {
    private enum Command : byte {
        UpdateMarriage = 0,
        UpdateHall = 1,
        Error = 2,
        Proposal = 3,
        Engaged = 6,
        DivorceRequest = 8,
        Reserve = 13,
        ConfirmReservation = 15,
        RequestCancelReservation = 16,
        DeclinedCancelReservation = 17,
        DeclineCancelReservation = 18,
        AcceptCancelReservation = 19,
        ChangeReservationRequest = 20,
        DeclineReservationChange = 21,
        AcceptReservationChange = 22,
        InvitationClear = 23,
        Ticket = 24,
        SearchInvitee = 27,
        SendInvite = 29,
        MutualAgree = 30,
        MutualCancel = 31,
    }

    public static ByteWriter UpdateMarriage(Marriage marriage) {
        var pWriter = Packet.Of(SendOp.Wedding);
        pWriter.Write<Command>(Command.UpdateMarriage);
        pWriter.WriteClass<Marriage>(marriage);

        return pWriter;
    }

    // Client: WeddingManager::OnTicketList (weddingmanager.cpp:236): the own hall's ticket, then an int count
    // and that many pairs of two longs, the invitations already sent. CanInvite (:417) looks a new guest up in
    // them by the two ids the guest dialog holds. The pair is written as account id, character id, the order
    // a guest is sent in (WeddingGuest::Write, weddingproxy.cpp:456); which of the two the dialog's script passes
    // first is not in the client code, so the client's own already-invited test is unproven. The server tests
    // by character id.
    public static ByteWriter UpdateHall(WeddingHall hall) {
        var pWriter = Packet.Of(SendOp.Wedding);
        pWriter.WriteByte((byte) Command.UpdateHall);
        pWriter.WriteClass<WeddingHall>(hall);
        pWriter.WriteInt(hall.GuestList.Count);
        foreach ((long characterId, long accountId) in hall.GuestList) {
            pWriter.WriteLong(accountId);
            pWriter.WriteLong(characterId);
        }

        return pWriter;
    }

    // Client: UIWeddingGuestDialog::OnStrangerFound (uiweddingguestdialog.cpp:264) reads the account id, the
    // character id and the name of the character that Wedding 0x1b asked for; character id 0 shows the
    // not-found notice.
    public static ByteWriter InvitationFound(long accountId, long characterId, string name) {
        var pWriter = Packet.Of(SendOp.Wedding);
        pWriter.WriteByte((byte) Command.SearchInvitee);
        pWriter.WriteLong(accountId);
        pWriter.WriteLong(characterId);
        pWriter.WriteUnicodeString(name);

        return pWriter;
    }

    // Client: WeddingManager::OnInvitationOk (weddingmanager.cpp:296) reads nothing: the notice that the
    // invitations were sent, and the invitation dialog closes.
    public static ByteWriter InvitationOk() {
        var pWriter = Packet.Of(SendOp.Wedding);
        pWriter.WriteByte((byte) Command.SendInvite);

        return pWriter;
    }

    public static ByteWriter Error(WeddingError error) {
        var pWriter = Packet.Of(SendOp.Wedding);
        pWriter.Write<Command>(Command.Error);
        // The client reads the result as one byte and nothing after it (WeddingManager::OnResult).
        pWriter.WriteByte((byte) error);

        return pWriter;
    }

    // The partner asks for a mutual divorce. Client: WeddingManager::OnDivorceRequest (weddingmanager.cpp:317)
    // reads nothing after the mode and opens a yes/no box with the partner's name; the answer is Wedding 9.
    public static ByteWriter DivorceRequest() {
        var pWriter = Packet.Of(SendOp.Wedding);
        pWriter.WriteByte((byte) Command.DivorceRequest);

        return pWriter;
    }

    public static ByteWriter Proposal(string name, long characterId) {
        var pWriter = Packet.Of(SendOp.Wedding);
        pWriter.Write<Command>(Command.Proposal);
        pWriter.WriteLong(characterId);
        pWriter.WriteUnicodeString(name);

        return pWriter;
    }

    public static ByteWriter Engaged(Marriage marriage) {
        var pWriter = Packet.Of(SendOp.Wedding);
        pWriter.Write<Command>(Command.Engaged);
        pWriter.WriteClass<Marriage>(marriage);

        return pWriter;
    }

    public static ByteWriter Reserve(WeddingHall hall) {
        var pWriter = Packet.Of(SendOp.Wedding);
        pWriter.Write<Command>(Command.Reserve);
        pWriter.WriteClass<WeddingHall>(hall);

        return pWriter;
    }

    public static ByteWriter ConfirmReservation(WeddingHall hall) {
        var pWriter = Packet.Of(SendOp.Wedding);
        pWriter.Write<Command>(Command.ConfirmReservation);
        pWriter.WriteClass<WeddingHall>(hall);

        return pWriter;
    }

    public static ByteWriter RequestCancelReservation() {
        var pWriter = Packet.Of(SendOp.Wedding);
        pWriter.Write<Command>(Command.RequestCancelReservation);

        return pWriter;
    }

    public static ByteWriter DeclineCancelReservation() {
        var pWriter = Packet.Of(SendOp.Wedding);
        pWriter.Write<Command>(Command.DeclineCancelReservation);

        return pWriter;
    }

    public static ByteWriter DeclinedCancelReservation(WeddingError reply) {
        var pWriter = Packet.Of(SendOp.Wedding);
        pWriter.Write<Command>(Command.DeclinedCancelReservation);
        pWriter.Write<WeddingError>(reply);

        return pWriter;
    }

    public static ByteWriter AcceptCancelReservation(WeddingError reply) {
        var pWriter = Packet.Of(SendOp.Wedding);
        pWriter.Write<Command>(Command.AcceptCancelReservation);
        pWriter.Write<WeddingError>(reply);

        return pWriter;
    }

    public static ByteWriter ChangeReservationRequest(WeddingHall hall, long differenceCost) {
        var pWriter = Packet.Of(SendOp.Wedding);
        pWriter.Write<Command>(Command.ChangeReservationRequest);
        pWriter.WriteClass<WeddingHall>(hall);
        pWriter.WriteLong(differenceCost);

        return pWriter;
    }

    public static ByteWriter DeclineReservationChange() {
        var pWriter = Packet.Of(SendOp.Wedding);
        pWriter.Write<Command>(Command.DeclineReservationChange);

        return pWriter;
    }

    public static ByteWriter AcceptReservationChange(WeddingHall hall, long differenceCost) {
        var pWriter = Packet.Of(SendOp.Wedding);
        pWriter.Write<Command>(Command.AcceptReservationChange);
        pWriter.WriteClass<WeddingHall>(hall);
        pWriter.WriteLong(differenceCost);

        return pWriter;
    }

    // Client: WeddingManager::OnInvitationTicket (weddingmanager.cpp:259): the id the ticket is kept under, which
    // the client sends back in Wedding 0x1a to enter, then the hall ticket.
    public static ByteWriter Ticket(long marriageId, WeddingHall hall) {
        var pWriter = Packet.Of(SendOp.Wedding);
        pWriter.WriteByte((byte) Command.Ticket);
        pWriter.WriteLong(marriageId);
        pWriter.WriteClass<WeddingHall>(hall);

        return pWriter;
    }

    // Client: WeddingManager::OnMode1e (weddingmanager.cpp:341): a byte; 1 and 2 open the yes/no box of the
    // start and the end of the ceremony, 3 is followed by the partner's name and starts the steps in which the
    // character types it.
    public static ByteWriter MutualAgree(WeddingAgreeType type, string partnerName) {
        var pWriter = Packet.Of(SendOp.Wedding);
        pWriter.WriteByte((byte) Command.MutualAgree);
        pWriter.WriteByte((byte) type);
        if (type == WeddingAgreeType.PartnerName) {
            pWriter.WriteUnicodeString(partnerName);
        }

        return pWriter;
    }

    // Client: WeddingManager::OnMode1f (weddingmanager.cpp:358): a byte; closes the open box of that question.
    public static ByteWriter MutualCancel(WeddingAgreeType type) {
        var pWriter = Packet.Of(SendOp.Wedding);
        pWriter.WriteByte((byte) Command.MutualCancel);
        pWriter.WriteByte((byte) type);

        return pWriter;
    }

    // Client: WeddingManager::OnInvitationClear (weddingmanager.cpp:282): no body; the client drops every hall
    // ticket it holds. KMS2 sends it after the marriage and the hall on a field entry.
    public static ByteWriter InvitationClear() {
        var pWriter = Packet.Of(SendOp.Wedding);
        pWriter.WriteByte((byte) Command.InvitationClear);

        return pWriter;
    }
}
