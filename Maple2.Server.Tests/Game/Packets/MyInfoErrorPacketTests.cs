using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

public class MyInfoErrorPacketTests {
    // Client: CSceneStateGame::OnMyInfo (scenestategame.cpp:4195) reads int object id, text; for the local player
    // int result, text. It returns before the result when no player has that object id.
    [Test]
    public void ErrorCarriesTheReceiverObjectId() {
        ByteWriter writer = MyInfoPacket.Error(0x11223344, "x");
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.MyInfo));
        Assert.That(reader.ReadInt(), Is.EqualTo(0x11223344));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo(string.Empty));
        Assert.That(reader.ReadInt(), Is.Not.EqualTo(0));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("x"));
        Assert.That(writer.Length, Is.EqualTo(2 + 4 + 2 + 4 + 2 + 2));
    }

    [Test]
    public void BanMottoCarriesTheReceiverObjectId() {
        ByteWriter writer = MyInfoPacket.BanMotto(7, "ab");
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.MyInfo));
        Assert.That(reader.ReadInt(), Is.EqualTo(7));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("ab"));
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo(string.Empty));
    }
}
