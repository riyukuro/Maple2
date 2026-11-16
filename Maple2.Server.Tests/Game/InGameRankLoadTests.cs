using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game;

// InGameRank 0x00EE at login, as CGameRankingCli reads it (FUN_140b2ab70): u8 31 (the only value it takes), then
// i32 refresh period in seconds and i32 period in minutes.
public class InGameRankLoadTests {
    [Test]
    public void Load_IsTheKindAndTheTwoPeriods() {
        byte[] packet = InGameRankPacket.Load().ToArray();

        Assert.That(TestGameSession.Op(packet), Is.EqualTo(SendOp.InGameRank));
        var reader = new ByteReader(packet, 2);
        Assert.Multiple(() => {
            Assert.That(reader.ReadByte(), Is.EqualTo(31));
            Assert.That(reader.ReadInt(), Is.EqualTo(120));
            Assert.That(reader.ReadInt(), Is.EqualTo(60));
            Assert.That(reader.Available, Is.EqualTo(0));
        });
    }
}
