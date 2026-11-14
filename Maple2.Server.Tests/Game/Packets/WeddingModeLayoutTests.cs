using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// Wedding modes as WeddingManager::OnPacket (weddingmanager.cpp:198) dispatches and reads them.
public class WeddingModeLayoutTests {
    private static WeddingHall Hall() => new() {
        Id = 11,
        MarriageId = 12,
        CeremonyTime = 13,
        PackageId = 2,
        PackageHallId = 3,
        Public = true,
        ReserverAccountId = 14,
        ReserverCharacterId = 15,
        ReserverName = "a",
        PartnerName = "b",
    };

    private static ByteReader Open(ByteWriter packet, byte mode) {
        var reader = new ByteReader(packet.Buffer, 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Wedding));
        Assert.That(reader.ReadByte(), Is.EqualTo(mode));
        return reader;
    }

    // WeddingHallTicket::Read (weddingproxy.cpp:387): the fee is four longs and a total, then the coupon id.
    private static void ReadTicket(ByteReader reader) {
        Assert.That(reader.ReadLong(), Is.EqualTo(11));
        Assert.That(reader.ReadInt(), Is.EqualTo(2));
        Assert.That(reader.ReadInt(), Is.EqualTo(3));
        Assert.That(reader.ReadLong(), Is.EqualTo(13));
        Assert.That(reader.ReadBool(), Is.True);
        Assert.That(reader.ReadLong(), Is.EqualTo(14));
        Assert.That(reader.ReadLong(), Is.EqualTo(15));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("a"));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("b"));
        for (int i = 0; i < 5; i++) {
            reader.ReadLong();
        }
        reader.ReadInt();
    }

    // 0x11 OnReservationChangeRequest: a short reason, 10 or 0x22 open the no-refund box.
    [Test]
    public void NoRefundCancelOffer_ShortReason() {
        foreach (WeddingError reason in new[] { WeddingReservationRules.NoRefundDeclined, WeddingReservationRules.NoRefundNoPartner }) {
            ByteWriter packet = WeddingPacket.NoRefundCancelOffer(reason);
            ByteReader reader = Open(packet, 0x11);
            Assert.That(reader.ReadShort(), Is.EqualTo((short) reason));
            Assert.That(reader.Position, Is.EqualTo(packet.Length));
        }
        Assert.That(new[] { (short) WeddingReservationRules.NoRefundDeclined, (short) WeddingReservationRules.NoRefundNoPartner }, Is.EqualTo(new short[] { 10, 0x22 }));
    }

    // 0x14 OnMode14 and 0x16 OnMode16: the ticket, then a long cost.
    [TestCase(true, (byte) 0x14)]
    [TestCase(false, (byte) 0x16)]
    public void ReservationChange_TicketThenCost(bool request, byte mode) {
        ByteWriter packet = request ? WeddingPacket.ReservationChangeRequest(Hall(), 300) : WeddingPacket.ReservationChanged(Hall(), 300);
        ByteReader reader = Open(packet, mode);
        ReadTicket(reader);
        Assert.That(reader.ReadLong(), Is.EqualTo(300));
        Assert.That(reader.Position, Is.EqualTo(packet.Length));
    }

    // 0x1b goes to UIWeddingGuestDialog::OnStrangerFound; 0x1d OnInvitationOk reads nothing.
    [Test]
    public void Invitation_FoundAndOk() {
        ByteWriter found = WeddingPacket.InvitationFound(1, 2, "c");
        ByteReader reader = Open(found, 0x1b);
        Assert.That(reader.ReadLong(), Is.EqualTo(1));
        Assert.That(reader.ReadLong(), Is.EqualTo(2));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("c"));
        Assert.That(reader.Position, Is.EqualTo(found.Length));

        ByteWriter ok = WeddingPacket.InvitationOk();
        Assert.That(Open(ok, 0x1d).Position, Is.EqualTo(ok.Length));
    }
}
