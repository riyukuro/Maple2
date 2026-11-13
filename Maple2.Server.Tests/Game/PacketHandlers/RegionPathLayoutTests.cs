using System.Numerics;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.PacketHandlers;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// Skill 1 2 after the cast id and the attack point, as the client writes it (CMagic::FUN_141466da0): the cast's
// TickSync count, the arrow's index in its attack, the end position and the direction. Sticky Monster 10600281 fires
// five arrows, so one cast sends indexes 0 to 4; a held cast (Focused Fire 10600181) counts its TickSyncs up.
public class RegionPathLayoutTests {
    [Test]
    public void ReadsTheSendCountAndTheArrowIndex() {
        ByteReader packet = TestGameSession.Packet(writer => {
            writer.WriteInt(2);
            writer.WriteInt(4);
            writer.WriteFloat(100f);
            writer.WriteFloat(-50f);
            writer.WriteFloat(1500f);
            writer.WriteFloat(0f);
            writer.WriteFloat(1f);
            writer.WriteFloat(0f);
        });
        SkillHandler.RegionPath path = SkillHandler.ReadRegionPath(packet);
        Assert.Multiple(() => {
            Assert.That(path.SendCount, Is.EqualTo(2));
            Assert.That(path.Arrow, Is.EqualTo(4));
            Assert.That(path.Position, Is.EqualTo(new Vector3(100f, -50f, 1500f)));
            Assert.That(path.Rotation, Is.EqualTo(new Vector3(0f, 1f, 0f)));
            Assert.That(packet.Available, Is.EqualTo(0));
        });
    }
}
