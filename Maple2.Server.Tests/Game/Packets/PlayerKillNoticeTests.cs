using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

public class PlayerKillNoticeTests {
    // Client: CSceneStateGame::OnPlayerKillNotice (client/ms2/scenestategame.cpp:4344): byte mode; 0: two unicode
    // texts (killer, victim); 1: an interface text.
    [Test]
    public void KillIsModeThenTwoNames() {
        ByteWriter writer = PlayerKillNoticePacket.Kill("ab", "cde");
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.PlayerKillNotice));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("ab"));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("cde"));
        Assert.That(writer.Length, Is.EqualTo(2 + 1 + 2 + 4 + 2 + 6));
    }

    [Test]
    public void NoticeIsModeThenInterfaceText() {
        ByteWriter writer = PlayerKillNoticePacket.Notice(new InterfaceText("hi"));
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.PlayerKillNotice));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadBool(), Is.False);
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("hi"));
        Assert.That(writer.Length, Is.EqualTo(2 + 1 + 1 + 4 + 2 + 4));
    }
}
