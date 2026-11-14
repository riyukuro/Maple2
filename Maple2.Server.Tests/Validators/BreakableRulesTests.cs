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

    // A breakable built invisible has the Hide state; SetVisibleBreakableObject(ids, true) shows it, and it breaks.
    [Test]
    public void BoxShownByATriggerBreaks() {
        var actor = new BreakableActor(Visible: false, Id: 5, HideTime: 0, ResetTime: 0, GlobalDropBoxId: 0, Position: default, Rotation: default);
        var breakable = new FieldBreakable(null!, 1, "abc", actor);
        Assert.That(breakable.State, Is.EqualTo(BreakableState.Hide));

        Assert.That(breakable.SetVisible(true), Is.True);

        Assert.That(breakable.State, Is.EqualTo(BreakableState.Show));
        Assert.That(BreakableRules.CanBreak(breakable.State), Is.True);
    }

    // The mirror case: one built shown and hidden by the trigger still takes a hit.
    [Test]
    public void BoxBuiltShownBreaks() {
        var actor = new BreakableActor(Visible: true, Id: 5, HideTime: 0, ResetTime: 0, GlobalDropBoxId: 0, Position: default, Rotation: default);
        var breakable = new FieldBreakable(null!, 1, "abc", actor);
        breakable.SetVisible(false);
        Assert.That(BreakableRules.CanBreak(breakable.State), Is.True);
    }

    // KR 02000011_bf: the lever holds its platform 30 s on a 20 s cycle. Stopped at tick 100000 with offset 2000,
    // the client holds clock 102000; restarted at 130000 the added time is -30000, so the clock goes on from 102000
    // instead of jumping to 132000 (10 s further along the path).
    [Test]
    public void Cycle_ARestartGoesOnFromTheStop() {
        int frozen = BreakableCycle.Clock(100000, 2000, 0);
        int added = BreakableCycle.Resume(0, 100000, 130000);
        Assert.Multiple(() => {
            Assert.That(frozen, Is.EqualTo(102000));
            Assert.That(added, Is.EqualTo(-30000));
            Assert.That(BreakableCycle.Clock(130000, 2000, added), Is.EqualTo(frozen));
            // A second stop and start add up.
            Assert.That(BreakableCycle.Resume(added, 140000, 145000), Is.EqualTo(-35000));
            // The tick wraps as the client's unsigned clock does.
            Assert.That(BreakableCycle.Clock(int.MaxValue, 1, 0), Is.EqualTo(int.MinValue));
        });
    }
}
