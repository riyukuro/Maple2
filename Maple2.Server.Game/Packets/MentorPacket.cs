using Maple2.Database.Extensions;
using Maple2.Model.Enum;
using Maple2.Model.Game;
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
        PairMode = 15,
        Unknown16 = 16,
    }

    public static ByteWriter UpdateRole(MentorRole role, int objectId) {
        var pWriter = Packet.Of(SendOp.Mentor);
        pWriter.WriteByte((byte) Command.UpdateRole);
        pWriter.WriteByte((byte) role);
        pWriter.WriteInt(objectId);

        return pWriter;
    }

    // A mentoring row as CMentor::Read (commonfiles/mentor.cpp:230) reads it: the row's id (the client matches the
    // accept and decline answers on it), a long it does not read, the other character's id (the online flags of
    // Mentor 15 match on it), name, level, profile picture, an int it does not read, online and the job.
    public record Entry(long Id, PlayerInfo Partner);

    private static void WriteEntry(ByteWriter pWriter, Entry entry) {
        pWriter.WriteLong(entry.Id);
        pWriter.WriteLong(0);
        pWriter.WriteLong(entry.Partner.CharacterId);
        pWriter.WriteUnicodeString(entry.Partner.Name);
        pWriter.WriteShort(entry.Partner.Level);
        pWriter.WriteUnicodeString(entry.Partner.Picture);
        pWriter.WriteInt(0);
        pWriter.WriteBool(entry.Partner.Online);
        pWriter.WriteInt((int) entry.Partner.Job);
    }

    private static ByteWriter Entries(Command command, IReadOnlyCollection<Entry> entries) {
        var pWriter = Packet.Of(SendOp.Mentor);
        pWriter.WriteByte((byte) command);
        pWriter.WriteInt(entries.Count);
        foreach (Entry entry in entries) {
            WriteEntry(pWriter, entry);
        }

        return pWriter;
    }

    // CMentoringManager::OnMentorList (mentoringmanager.cpp:320): the mentors' requests waiting for the mentee's answer.
    public static ByteWriter MenteeInvitations(params Entry[] requests) => Entries(Command.MenteeInvitations, requests);

    // CMentoringManager::OnMenteeList (mentoringmanager.cpp:333): the pairs, the mentor of a mentee or the mentees of a
    // mentor.
    public static ByteWriter MyList(params Entry[] pairs) => Entries(Command.MyList, pairs);

    public static ByteWriter AssignReturningUser() {
        var pWriter = Packet.Of(SendOp.Mentor);
        pWriter.WriteByte((byte) Command.AssignReturningUser);

        return pWriter;
    }

    public static ByteWriter MenteeInvite(long characterId, string name) {
        var pWriter = Packet.Of(SendOp.Mentor);
        pWriter.WriteByte((byte) Command.SendMenteeInvite);
        pWriter.WriteLong(characterId);
        pWriter.WriteUnicodeString(name);

        return pWriter;
    }

    // CMentoringManager::OnMentorApproved: the accepted request's id (s_mentoring_invite_approve_mentor).
    public static ByteWriter AcceptMentorInvite(long id) {
        var pWriter = Packet.Of(SendOp.Mentor);
        pWriter.WriteByte((byte) Command.AcceptMentorInvite);
        pWriter.WriteLong(id);

        return pWriter;
    }

    // CMentoringManager::OnMentorDeclined: the declined request's id (s_mentoring_invite_decline_mentor).
    public static ByteWriter DeclineMentorInvite(long id) {
        var pWriter = Packet.Of(SendOp.Mentor);
        pWriter.WriteByte((byte) Command.DeclineMentorInvite);
        pWriter.WriteLong(id);

        return pWriter;
    }

    public static ByteWriter AssignMentor() {
        var pWriter = Packet.Of(SendOp.Mentor);
        pWriter.WriteByte((byte) Command.AssignMentor);

        return pWriter;
    }

    public static ByteWriter Load() {
        var pWriter = Packet.Of(SendOp.Mentor);
        pWriter.WriteByte((byte) Command.Load);

        return pWriter;
    }

    public static ByteWriter LoginPoints() {
        var pWriter = Packet.Of(SendOp.Mentor);
        pWriter.WriteByte((byte) Command.LoginPoints);
        pWriter.WriteInt(100); // points received
        pWriter.WriteLong(DateTime.Now.AddDays(-0).ToEpochSeconds());

        return pWriter;
    }

    public static ByteWriter DailyPoints() {
        var pWriter = Packet.Of(SendOp.Mentor);
        pWriter.WriteByte((byte) Command.DailyPoints);
        pWriter.WriteInt(1000);
        pWriter.WriteInt(0); // mentee points received today
        pWriter.WriteLong(DateTime.Now.AddDays(+2).ToEpochSeconds()); // reset timestamp

        return pWriter;
    }

    public static ByteWriter Unknown12() {
        var pWriter = Packet.Of(SendOp.Mentor);
        pWriter.WriteByte((byte) Command.Unknown12);
        pWriter.WriteLong();
        pWriter.WriteLong();

        return pWriter;
    }

    // CMentoringManager::OnMentorRequest: the mentor's name (s_mentoring_notify_mentor_request) and the request.
    public static ByteWriter ReceiveMenteeInvite(Entry request) {
        var pWriter = Packet.Of(SendOp.Mentor);
        pWriter.WriteByte((byte) Command.ReceiveMenteeInvite);
        pWriter.WriteUnicodeString(request.Partner.Name);
        WriteEntry(pWriter, request);

        return pWriter;
    }

    // CMentoringManager::OnPairMode: per pair the other character's id and whether it is online.
    public static ByteWriter PairOnline(IReadOnlyCollection<Entry> pairs) {
        var pWriter = Packet.Of(SendOp.Mentor);
        pWriter.WriteByte((byte) Command.PairMode);
        pWriter.WriteInt(pairs.Count);
        foreach (Entry pair in pairs) {
            pWriter.WriteLong(pair.Partner.CharacterId);
            pWriter.WriteBool(pair.Partner.Online);
        }

        return pWriter;
    }

    // CMentoringManager::OnError (mentoringmanager.cpp:500): a byte result, which picks the notice, and an int.
    public static ByteWriter Error(MentorError error) {
        var pWriter = Packet.Of(SendOp.Mentor);
        pWriter.WriteByte((byte) Command.Unknown16);
        pWriter.WriteByte((byte) error);
        pWriter.WriteInt(0);

        return pWriter;
    }
}
