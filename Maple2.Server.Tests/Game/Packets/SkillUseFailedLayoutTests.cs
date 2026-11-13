using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// SkillUseFailed 0x0040 (the client reads modes 5 and 6 only): byte mode, long cast uid, int skill id, byte error, int cooldown start tick,
// short cooldown length in 0.1 s, int recharge uses.
public class SkillUseFailedLayoutTests {
    [Test]
    public void RunningCooldown() {
        var cooldown = new SkillCooldown(10500061, 1) { EndTick = 50_000, Charges = 2 };
        ByteWriter writer = SkillUseFailedPacket.Fail(77, 10500061, 12.5f, cooldown, 45_000);
        var reader = new ByteReader(writer.Buffer, 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.SkillUseFailed));
        Assert.That(reader.ReadByte(), Is.EqualTo(5));
        Assert.That(reader.ReadLong(), Is.EqualTo(77));
        Assert.That(reader.ReadInt(), Is.EqualTo(10500061));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(37_500));
        Assert.That(reader.ReadShort(), Is.EqualTo(125));
        Assert.That(reader.ReadInt(), Is.EqualTo(2));
        Assert.That(writer.Length, Is.EqualTo(2 + 1 + 8 + 4 + 1 + 4 + 2 + 4));
    }

    // No cooldown held, or one that has ended: length 0 and 0 uses, which clears the client's cooldown.
    [TestCase(false)]
    [TestCase(true)]
    public void NoCooldown(bool ended) {
        SkillCooldown? cooldown = ended ? new SkillCooldown(10500061, 1) { EndTick = 40_000 } : null;
        ByteWriter writer = SkillUseFailedPacket.Fail(77, 10500061, 12.5f, cooldown, 45_000);
        var reader = new ByteReader(writer.Buffer, 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.SkillUseFailed));
        Assert.That(reader.ReadByte(), Is.EqualTo(5));
        Assert.That(reader.ReadLong(), Is.EqualTo(77));
        Assert.That(reader.ReadInt(), Is.EqualTo(10500061));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(reader.ReadShort(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(writer.Length, Is.EqualTo(26));
    }

    // Mode 6 (a state skill not taken): the state after the error byte, then the cooldown as in mode 5.
    [Test]
    public void StateFail() {
        ByteWriter writer = SkillUseFailedPacket.StateFail(77, 10500061, 43, 0f, null, 45_000);
        var reader = new ByteReader(writer.Buffer, 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.SkillUseFailed));
        Assert.That(reader.ReadByte(), Is.EqualTo(6));
        Assert.That(reader.ReadLong(), Is.EqualTo(77));
        Assert.That(reader.ReadInt(), Is.EqualTo(10500061));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(43));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(reader.ReadShort(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(writer.Length, Is.EqualTo(30));
    }
}
