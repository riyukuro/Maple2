using System.Collections.Generic;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// The invitations to a wedding hall: Wedding 0x1b (find), 0x1c (send), the invited list of sub-type 1 and the
// guest's ticket.
public class WeddingInviteRulesTests {
    private const long Hall = 5;
    private const long Time = 100000;
    private const long Now = Time - 3600;
    private static readonly Dictionary<long, long> Invited = new() { [33] = 3 };

    private static (bool Send, WeddingError Error, long Cost) Check(long[] guests, int known = -1, long meso = 5000, long now = Now, long hall = Hall, int max = 3) {
        List<long> list = WeddingInviteRules.Guests(guests);
        return WeddingInviteRules.Check(hall, Time, now, Invited, list, known < 0 ? list.Count : known, max, meso, 1000);
    }

    [Test]
    public void Guests_EachCharacterOnceWithoutZero() {
        Assert.That(WeddingInviteRules.Guests([44, 0, 55, 44]), Is.EqualTo(new List<long> { 44, 55 }));
    }

    [Test]
    public void Check_TakesCountTimesThePrice() {
        Assert.That(Check([44, 55]), Is.EqualTo((true, WeddingError.none, 2000L)));
        Assert.That(Check([44, 55], meso: 2000), Is.EqualTo((true, WeddingError.none, 2000L)));
        Assert.That(Check([44, 55], meso: 1999), Is.EqualTo((false, WeddingError.s_wedding_result_lack_meso_invitation, 0L)));
    }

    [Test]
    public void Check_RefusesWithTheClientsResults() {
        Assert.That(Check([44], hall: 0).Error, Is.EqualTo(WeddingError.s_wedding_result_not_find_weddinghall));
        Assert.That(Check([44, 55, 66]).Error, Is.EqualTo(WeddingError.s_wedding_result_wedding_invitation_count), "1 invited + 3 > 3");
        Assert.That(Check([44, 33]).Error, Is.EqualTo(WeddingError.s_wedding_result_already_send_invitation));
    }

    // No result text in the client for these, so nothing is stored, taken or sent back.
    [Test]
    public void Check_SilentRefusals() {
        Assert.That(Check([]), Is.EqualTo((false, WeddingError.none, 0L)));
        Assert.That(Check([44], known: 0), Is.EqualTo((false, WeddingError.none, 0L)), "not a character");
        Assert.That(Check([44], now: Time - 600), Is.EqualTo((false, WeddingError.none, 0L)), "10 minutes before");
        Assert.That(Check([44], now: Time - 601).Send, Is.True);
    }

    [Test]
    public void GuestTicket_InvitedInTheWindowAndNotTheOwnHall() {
        Assert.That(WeddingInviteRules.GuestTicket(Invited, 33, 7, 0, Time, Time - 1800, 1800), Is.True);
        Assert.That(WeddingInviteRules.GuestTicket(Invited, 33, 7, 0, Time, Time + 1800, 1800), Is.True);
        Assert.That(WeddingInviteRules.GuestTicket(Invited, 33, 7, 0, Time, Time - 1801, 1800), Is.False);
        Assert.That(WeddingInviteRules.GuestTicket(Invited, 44, 7, 0, Time, Time, 1800), Is.False, "not invited");
        Assert.That(WeddingInviteRules.GuestTicket(Invited, 33, 7, 7, Time, Time, 1800), Is.False, "the couple has its own ticket");
    }

    private static WeddingHall MakeHall() => new() {
        Id = Hall,
        MarriageId = 7,
        ReserverAccountId = 1,
        ReserverCharacterId = 11,
        ReserverName = "a",
        PartnerName = "b",
        CeremonyTime = Time,
    };

    // WeddingManager::OnTicketList: the ticket, an int count, then two longs per invitation.
    [Test]
    public void UpdateHall_EndsWithTheInvitedPairs() {
        WeddingHall hall = MakeHall();
        int empty = WeddingPacket.UpdateHall(hall).Length;
        hall.GuestList = new Dictionary<long, long> { [33] = 3, [44] = 4 };
        ByteWriter packet = WeddingPacket.UpdateHall(hall);
        Assert.That(packet.Length, Is.EqualTo(empty + 32));

        var reader = new ByteReader(packet.Buffer, empty - 4);
        Assert.That(reader.ReadInt(), Is.EqualTo(2));
        Assert.That(reader.ReadLong(), Is.EqualTo(3), "account id");
        Assert.That(reader.ReadLong(), Is.EqualTo(33), "character id");
        Assert.That(reader.ReadLong(), Is.EqualTo(4));
        Assert.That(reader.ReadLong(), Is.EqualTo(44));
    }

    // UIWeddingGuestDialog::OnStrangerFound: account id, character id, name; OnInvitationOk reads nothing.
    [Test]
    public void InvitationFoundAndOk_Layout() {
        ByteWriter packet = WeddingPacket.InvitationFound(3, 33, "c");
        var reader = new ByteReader(packet.Buffer, 0);
        reader.ReadShort();
        Assert.That(reader.ReadByte(), Is.EqualTo(0x1b));
        Assert.That(reader.ReadLong(), Is.EqualTo(3));
        Assert.That(reader.ReadLong(), Is.EqualTo(33));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("c"));
        Assert.That(packet.Length, Is.EqualTo(2 + 1 + 8 + 8 + 2 + 2));

        packet = WeddingPacket.InvitationOk();
        reader = new ByteReader(packet.Buffer, 0);
        reader.ReadShort();
        Assert.That(reader.ReadByte(), Is.EqualTo(0x1d));
        Assert.That(packet.Length, Is.EqualTo(3));
    }
}
