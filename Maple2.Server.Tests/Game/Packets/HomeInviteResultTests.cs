using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

public class HomeInviteResultTests {
    // Client: HomeInvite mode 0 (scenestategame.cpp:6637) reads byte result, text name.
    [Test]
    public void HomeInviteResultIsCodeThenName() {
        ByteWriter writer = HomeInvitePacket.Result(HomeInvitePacket.InviteResult.Declined, "ab");
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.HomeInvite));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadByte(), Is.EqualTo(2));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("ab"));
        Assert.That(writer.Length, Is.EqualTo(2 + 1 + 1 + 2 + 4));
    }

    [Test]
    public void HomeInviteResultCodesAreTheClientCases() {
        Assert.That((byte) HomeInvitePacket.InviteResult.Accepted, Is.EqualTo(1));
        Assert.That((byte) HomeInvitePacket.InviteResult.Waiting, Is.EqualTo(3));
        Assert.That((byte) HomeInvitePacket.InviteResult.Offline, Is.EqualTo(4));
        Assert.That((byte) HomeInvitePacket.InviteResult.AutoDeclined, Is.EqualTo(5));
        Assert.That((byte) HomeInvitePacket.InviteResult.TimedOut, Is.EqualTo(6));
        Assert.That((byte) HomeInvitePacket.InviteResult.Self, Is.EqualTo(7));
        Assert.That((byte) HomeInvitePacket.InviteResult.CannotInviteNow, Is.EqualTo(9));
    }
}
