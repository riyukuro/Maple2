using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Model;

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

    [TestCase(BreakableState.Show, true)]
    [TestCase(BreakableState.Break, false)] // a second hit packet on a broken one
    [TestCase(BreakableState.Hide, true)]
    [TestCase(BreakableState.Unknown5, true)]
    public void OnlyABrokenBreakableIsRefused(BreakableState state, bool expected) {
        Assert.That(BreakableRules.CanBreak(state), Is.EqualTo(expected));
    }

    // A breakable built invisible has the Hide state; the trigger's SetVisibleBreakableObject sets only Visible.
    [Test]
    public void BoxShownByATriggerBreaks() {
        var actor = new BreakableActor(Visible: false, Id: 5, HideTime: 0, ResetTime: 0, GlobalDropBoxId: 0, Position: default, Rotation: default);
        var breakable = new FieldBreakable(null!, 1, "abc", actor);
        Assert.That(breakable.State, Is.EqualTo(BreakableState.Hide));

        breakable.Visible = true; // what SetVisibleBreakableObject(ids, true) does

        Assert.That(breakable.State, Is.EqualTo(BreakableState.Hide));
        Assert.That(BreakableRules.CanBreak(breakable.State), Is.True);
    }

    // The mirror case: SetBreakable(ids, true) alone gives Show with Visible still false.
    [Test]
    public void BoxBuiltShownBreaks() {
        var actor = new BreakableActor(Visible: true, Id: 5, HideTime: 0, ResetTime: 0, GlobalDropBoxId: 0, Position: default, Rotation: default);
        var breakable = new FieldBreakable(null!, 1, "abc", actor);
        breakable.Visible = false;
        Assert.That(BreakableRules.CanBreak(breakable.State), Is.True);
    }
}
