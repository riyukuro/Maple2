using Maple2.Model.Game.Club;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

public class ClubInviteReplyTests {
    // Client: mode 8 (client/ms2/clubcli.cpp:1125) reads the invite (long id, three strings) and an int result,
    // drops the listed invite, and shows nothing for result 0. The invitee gets it on decline as on accept.
    [Test]
    public void Mode8DropsTheInvite() {
        ByteWriter writer = ClubPacket.AcceptInvite(new ClubInvite {
            ClubId = 900,
            Name = "club",
            LeaderName = "lead",
            Invitee = "guest",
        });
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Club));
        Assert.That(reader.ReadByte(), Is.EqualTo(8));
        Assert.That(reader.ReadLong(), Is.EqualTo(900));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("club"));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("lead"));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("guest"));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(writer.Length, Is.EqualTo(2 + 1 + 8 + (2 + 8) + (2 + 8) + (2 + 10) + 4));
    }

    // Client: mode 9 (client/ms2/clubcli.cpp:1143) reads the long club id, the name, a byte and a byte; byte 0 puts
    // the name into the "has declined" notice, so the name is the character that replied.
    [TestCase(false, 0)]
    [TestCase(true, 1)]
    public void Mode9NamesTheReplier(bool accept, int type) {
        ByteWriter writer = ClubPacket.InviteNotification(900, "guest", accept);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Club));
        Assert.That(reader.ReadByte(), Is.EqualTo(9));
        Assert.That(reader.ReadLong(), Is.EqualTo(900));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("guest"));
        Assert.That(reader.ReadByte(), Is.EqualTo(type));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(writer.Length, Is.EqualTo(2 + 1 + 8 + (2 + 10) + 1 + 1));
    }
}
