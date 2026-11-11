using Maple2.Model.Enum;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Tests.Validators;

public class BreakableRulesTests {
    // Breakable 0x0027: entity id, i64 skill uid, i32 skill id, i16 level, u8 motion point, u8 attack point.
    [Test]
    public void HitPacketLayout() {
        var writer = new ByteWriter();
        writer.WriteString("abc");
        writer.WriteLong(11);
        writer.WriteInt(10000001);
        writer.WriteShort(1);
        writer.WriteByte(0);
        writer.WriteByte(2);
        var packet = new ByteReader(writer.ToArray(), 0);
        Assert.That(packet.ReadString(), Is.EqualTo("abc"));
        Assert.That(packet.ReadLong(), Is.EqualTo(11));
        Assert.That(packet.ReadInt(), Is.EqualTo(10000001));
        Assert.That(packet.ReadShort(), Is.EqualTo(1));
        Assert.That(packet.ReadByte(), Is.EqualTo(0));
        Assert.That(packet.ReadByte(), Is.EqualTo(2));
        Assert.That(packet.Available, Is.EqualTo(0));
    }

    [TestCase(BreakableState.Show, true, true)]
    [TestCase(BreakableState.Show, false, false)]
    [TestCase(BreakableState.Break, true, false)] // a second hit packet on a broken one
    [TestCase(BreakableState.Hide, true, false)]
    [TestCase(BreakableState.Unknown5, true, false)]
    public void OnlyAShownBreakableBreaks(BreakableState state, bool visible, bool expected) {
        Assert.That(BreakableRules.CanBreak(state, visible), Is.EqualTo(expected));
    }
}
