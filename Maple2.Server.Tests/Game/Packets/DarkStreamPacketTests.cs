using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// Client: CSceneStateGame::OnDarkStream (0xC5) reads a byte command; 0 opens the score dialog at 0, 1 = int score,
// 2 = the result: UIDarkStreamResultDialog::ReadResultPacket reads five ints, a byte count and that many items.
public class DarkStreamPacketTests {
    [Test]
    public void Open_IsCommandZero() {
        ByteWriter writer = DarkStreamPacket.Open();
        var reader = new ByteReader(writer.Buffer, 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.DarkStream));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(writer.Length, Is.EqualTo(3));
    }

    [Test]
    public void Score_IsCommandOneAndAnInt() {
        ByteWriter writer = DarkStreamPacket.Score(-120);
        var reader = new ByteReader(writer.Buffer, 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.DarkStream));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo(-120));
        Assert.That(writer.Length, Is.EqualTo(2 + 1 + 4));
    }

    [Test]
    public void Result_IsScoreBestExpMesoHabiThenNoItems() {
        ByteWriter writer = DarkStreamPacket.Result(7100, 6200, 300, 400, 50);
        var reader = new ByteReader(writer.Buffer, 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.DarkStream));
        Assert.That(reader.ReadByte(), Is.EqualTo(2));
        Assert.That(reader.ReadInt(), Is.EqualTo(7100));
        Assert.That(reader.ReadInt(), Is.EqualTo(6200));
        Assert.That(reader.ReadInt(), Is.EqualTo(300));
        Assert.That(reader.ReadInt(), Is.EqualTo(400));
        Assert.That(reader.ReadInt(), Is.EqualTo(50));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(writer.Length, Is.EqualTo(2 + 1 + 5 * 4 + 1));
    }
}
