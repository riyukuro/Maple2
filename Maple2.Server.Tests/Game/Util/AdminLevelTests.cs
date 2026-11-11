using Maple2.Model.Enum;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Util;

public class AdminLevelTests {
    [TestCase(AdminPermissions.None, 1)]
    [TestCase(AdminPermissions.Warp, 1)]
    [TestCase(AdminPermissions.Debug | AdminPermissions.SpawnItem | AdminPermissions.SpawnNpc, 1)]
    [TestCase(AdminPermissions.GameMaster & ~AdminPermissions.Ban, 1)]
    [TestCase(AdminPermissions.GameMaster, 50)]
    [TestCase(AdminPermissions.GameMaster | AdminPermissions.Debug, 50)]
    [TestCase(AdminPermissions.Admin, 255)]
    public void Of_MapsPermissionsToTheClientLevel(AdminPermissions permissions, int level) {
        Assert.That(AdminLevel.Of(permissions), Is.EqualTo((byte) level));
        Assert.That(AdminLevel.IsGm(permissions), Is.EqualTo(level >= 50));
    }

    [Test]
    public void Packet_IsModeZeroAndTheLevel() {
        ByteWriter writer = AdminPacket.Level(AdminLevel.Of(AdminPermissions.None));

        // opcode, mode 0, level 1
        Assert.That(writer.Length, Is.EqualTo(4));
        Assert.That(writer.Buffer[2], Is.EqualTo(0));
        Assert.That(writer.Buffer[3], Is.EqualTo(1));
    }
}
