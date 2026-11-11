using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// Client: UIWorldChampionRewardDialog::OnPacket reads one mode byte; mode 11 reads nothing more.
public class WorldChampionPacketTests {
    [Test]
    public void NoSeasonIsOneByte() {
        ByteWriter writer = WorldChampionPacket.NoSeason();
        var reader = new ByteReader(writer.Buffer, 0);
        Assert.Multiple(() => {
            Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.WorldChampion));
            Assert.That(reader.ReadByte(), Is.EqualTo(11));
            Assert.That(reader.Position, Is.EqualTo(writer.Length));
            Assert.That(writer.Length, Is.EqualTo(2 + 1));
        });
    }

    // The modes the client writes and the opcodes it uses.
    [Test]
    public void Modes() {
        Assert.Multiple(() => {
            Assert.That(WorldChampionHandler.OpenMode, Is.EqualTo(8));
            Assert.That(WorldChampionHandler.MoveMode, Is.EqualTo(9));
            Assert.That((int) RecvOp.WorldChampion, Is.EqualTo(0x00AF));
            Assert.That((int) SendOp.WorldChampion, Is.EqualTo(0x011E));
            Assert.That(new WorldChampionHandler().OpCode, Is.EqualTo(RecvOp.WorldChampion));
        });
    }
}
