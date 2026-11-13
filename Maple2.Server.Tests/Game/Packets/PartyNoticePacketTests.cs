using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

public class PartyNoticePacketTests {
    // Client: CPartyManager::OnPartyNoticeWithSound (client/ms2/partymanager.cpp:1281): mode 0x15, an interface text,
    // then the sound key.
    [Test]
    public void NoticeWithSoundIsTextThenSound() {
        ByteWriter writer = PartyPacket.NoticeWithSound(new InterfaceText("hi"), "snd");
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.Multiple(() => {
            Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Party));
            Assert.That(reader.ReadByte(), Is.EqualTo(0x15));
            Assert.That(reader.ReadBool(), Is.False);
            Assert.That(reader.ReadInt(), Is.EqualTo(0));
            Assert.That(reader.ReadUnicodeString(), Is.EqualTo("hi"));
            Assert.That(reader.ReadUnicodeString(), Is.EqualTo("snd"));
            Assert.That(reader.Position, Is.EqualTo(writer.Length));
        });
    }

    // Client: CPartyManager::FUN_14162ee70 (client/ms2/partymanager.cpp:1357): mode 0x25, the member's id (long), the
    // name, then an interface text.
    [Test]
    public void MemberNoticeIsIdNameThenText() {
        ByteWriter writer = PartyPacket.MemberNotice(0x0102030405060708, "ab", new InterfaceText("cde"));
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.Multiple(() => {
            Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Party));
            Assert.That(reader.ReadByte(), Is.EqualTo(0x25));
            Assert.That(reader.ReadLong(), Is.EqualTo(0x0102030405060708));
            Assert.That(reader.ReadUnicodeString(), Is.EqualTo("ab"));
            Assert.That(reader.ReadBool(), Is.False);
            Assert.That(reader.ReadInt(), Is.EqualTo(0));
            Assert.That(reader.ReadUnicodeString(), Is.EqualTo("cde"));
            Assert.That(reader.Position, Is.EqualTo(writer.Length));
        });
        Assert.That(writer.Length, Is.EqualTo(2 + 1 + 8 + 2 + 4 + 1 + 4 + 2 + 6));
    }
}
