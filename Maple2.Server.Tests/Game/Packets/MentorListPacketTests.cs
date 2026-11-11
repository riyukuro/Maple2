using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

public class MentorListPacketTests {
    // Client: CMentoringManager::OnMentorList / OnMenteeList (mentoringmanager.cpp:320, :333): byte mode, int count,
    // then count entries. With no mentoring pairs kept by the server the lists carry no entry.
    [Test]
    public void MentorListIsEmpty() {
        ByteWriter writer = MentorPacket.MenteeInvitations();
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Mentor));
        Assert.That(reader.ReadByte(), Is.EqualTo(2));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(writer.Length, Is.EqualTo(2 + 1 + 4));
    }

    [Test]
    public void MyListIsEmpty() {
        ByteWriter writer = MentorPacket.MyList();
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Mentor));
        Assert.That(reader.ReadByte(), Is.EqualTo(3));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(writer.Length, Is.EqualTo(2 + 1 + 4));
    }
}
