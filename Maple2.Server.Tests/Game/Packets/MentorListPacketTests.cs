using System;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

public class MentorListPacketTests {
    // Client: CMentoringManager::OnMentorList / OnMenteeList (mentoringmanager.cpp:320, :333): byte mode, int count,
    // then count entries (CMentor::Read, commonfiles/mentor.cpp:230).
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

    // CMentor: the row id, a long, the other character's id, name, short level, profile picture, an int, online, job.
    [Test]
    public void AnEntryHoldsTheRowAndTheOtherCharacter() {
        var partner = new PlayerInfo(new CharacterInfo(1, 0x0102030405060708, "Ab", "", "p", Gender.Male, Job.Knight, 30), "", new AchievementInfo(), []) {
            Channel = 1,
        };
        ByteWriter writer = MentorPacket.MyList(new MentorPacket.Entry(7, partner));

        Assert.That(Convert.ToHexString(writer.Buffer, 2, writer.Length - 2), Is.EqualTo(
            "03" + "01000000" + "0700000000000000" + "0000000000000000" + "0807060504030201" + "0200" + "41006200" + "1E00"
            + "0100" + "7000" + "00000000" + "01" + "64000000"));
    }

    [Test]
    public void PairOnlineHoldsTheOtherCharacterAndItsFlag() {
        var partner = new PlayerInfo(new CharacterInfo(1, 9, "Ab", "", "p", Gender.Male, Job.Knight, 30), "", new AchievementInfo(), []) {
            Channel = -1,
        };
        ByteWriter writer = MentorPacket.PairOnline([new MentorPacket.Entry(7, partner)]);

        Assert.That(Convert.ToHexString(writer.Buffer, 2, writer.Length - 2), Is.EqualTo("0F" + "01000000" + "0900000000000000" + "00"));
    }
}
