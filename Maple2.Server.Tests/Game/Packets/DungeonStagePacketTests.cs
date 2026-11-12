using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// RoomStageDungeon 0xFF mode 1 (ClearStage, reads nothing after the mode) and DungeonMission 0x100 mode 3
// (RoomStageDungeonCli::OnStage, one byte) as KMS2 sends them.
public class DungeonStagePacketTests {
    [Test]
    public void ClearIsTheModeOnly() {
        Assert.That(RoomStageDungeonPacket.Clear().ToArray(), Is.EqualTo(new byte[] { 0xFF, 0x00, 0x01 }));
    }

    [Test]
    public void SetBytes() {
        Assert.That(RoomStageDungeonPacket.Set(0x01020304).ToArray(),
            Is.EqualTo(new byte[] { 0xFF, 0x00, 0x00, 0x04, 0x03, 0x02, 0x01, 0x00, 0x00 }));
    }

    [Test]
    public void StageBytes() {
        Assert.That(DungeonMissionPacket.Stage(1).ToArray(), Is.EqualTo(new byte[] { 0x00, 0x01, 0x03, 0x01 }));
    }
}
