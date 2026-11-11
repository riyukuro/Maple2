using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

public class DungeonHelpPacketTests {
    // Client: UIMessengerBrowser::FUN_141a230e0 (uimessenger.cpp:3018) reads, after mode 0, the int party id, the
    // leader's name, two longs (account and character), the int room id and a byte.
    [Test]
    public void HelperNoticeIsModeZero() {
        ByteWriter writer = DungeonHelpPacket.Helper(7, "n", 11, 12, 200, 3);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.DungeonHelp));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(7));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("n"));
        Assert.That(reader.ReadLong(), Is.EqualTo(11));
        Assert.That(reader.ReadLong(), Is.EqualTo(12));
        Assert.That(reader.ReadInt(), Is.EqualTo(200));
        Assert.That(reader.ReadByte(), Is.EqualTo(3));
        Assert.That(writer.Length, Is.EqualTo(2 + 1 + 4 + 4 + 8 + 8 + 4 + 1));
    }

    // Client: FUN_141a233d0 (uimessenger.cpp:3039) reads, after mode 1, needHelper and needMember bytes first.
    [Test]
    public void MemberNoticeIsModeOne() {
        ByteWriter writer = DungeonHelpPacket.Member(0, 300, 7, "n", 11, 12, 200, 2);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.DungeonHelp));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadByte(), Is.EqualTo(255)); // clamped to the byte
        Assert.That(reader.ReadInt(), Is.EqualTo(7));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("n"));
        Assert.That(reader.ReadLong(), Is.EqualTo(11));
        Assert.That(reader.ReadLong(), Is.EqualTo(12));
        Assert.That(reader.ReadInt(), Is.EqualTo(200));
        Assert.That(reader.ReadByte(), Is.EqualTo(2));
        Assert.That(writer.Length, Is.EqualTo(2 + 3 + 4 + 4 + 8 + 8 + 4 + 1));
    }

    // Client: CPartyManager::OnPartyFindDungeonCooldown (partymanager.cpp:1425) reads the int milliseconds left.
    [Test]
    public void CooldownCarriesTheMillisecondsLeft() {
        ByteWriter writer = PartyPacket.DungeonHelperCooldown(6000);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Party));
        Assert.That(reader.ReadByte(), Is.EqualTo(40));
        Assert.That(reader.ReadInt(), Is.EqualTo(6000));
        Assert.That(writer.Length, Is.EqualTo(2 + 1 + 4));
    }
}
