using System.Linq;
using System.Numerics;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Util;

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

    // The attack point packet's tags (StateSkill::SendSkill): a first tag writes its index m_n84
    // (stateskill.cpp:1381), a linked one m_n80 and then m_n84 (:1390-1391), and a linked bounce's m_n84 is 0
    // (:2777-2781). The region path an arrow sends carries that m_n84 (magicdata.cpp:1162), so the first tag's
    // byte and the linked tag's second byte declare the arrows.
    [Test]
    public void PointTagsDeclareTheArrowsTheirRegionPathsCarry() {
        ByteReader packet = TestGameSession.Packet(writer => {
            writer.WriteLong(11);
            writer.WriteInt(0);
            writer.WriteByte(2); // m_n84
            writer.WriteBool(true);
            writer.WriteLong(12);
            writer.WriteInt(0);
            writer.WriteByte(5); // m_n80
            writer.WriteByte(0); // m_n84
            writer.WriteBool(false);
        });
        var tags = SkillHandler.ReadPointTags(packet, 1);
        Assert.That(packet.Available, Is.EqualTo(0));
        Assert.That(tags, Has.Count.EqualTo(1));
        Assert.That(tags[0].Select(tag => tag.Uid), Is.EqualTo(new long[] { 11, 12 }));
        Assert.That(tags[0][1].PrevUid, Is.EqualTo(11));

        var budget = new SkillHitBudget();
        SkillHandler.DeclarePointTags(budget, 1, 3, tags);
        Assert.Multiple(() => {
            Assert.That(budget.TakeRegionPath(1, 3, 5, rounds: 1, tick: 10), Is.False, "a linked tag's m_n80 is no arrow");
            Assert.That(budget.TakeRegionPath(1, 3, 1, rounds: 1, tick: 10), Is.False);
            Assert.That(budget.TakeRegionPath(1, 2, 2, rounds: 1, tick: 10), Is.False, "another send count");
            Assert.That(budget.TakeRegionPath(1, 3, 2, rounds: 1, tick: 10), Is.True, "the first tag");
            Assert.That(budget.TakeRegionPath(1, 3, 0, rounds: 1, tick: 10), Is.True, "the linked bounce");
            Assert.That(budget.TakeTarget(11, 1, 0), Is.True);
            Assert.That(budget.TakeTarget(12, 1, 0), Is.True);
        });
    }
}
