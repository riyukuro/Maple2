using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Tests.Validators;

public class ScrollRulesTests {
    // ItemRepack 0x008C mode 1: u8 mode, i64 scroll uid, i64 target uid.
    [Test]
    public void RepackPacket() {
        var writer = new ByteWriter();
        writer.WriteByte(1);
        writer.WriteLong(111);
        writer.WriteLong(222);
        var packet = new ByteReader(writer.ToArray(), 0);
        Assert.That(packet.ReadByte(), Is.EqualTo(1));
        Assert.That(packet.ReadLong(), Is.EqualTo(111));
        Assert.That(packet.ReadLong(), Is.EqualTo(222));
        Assert.That(packet.Available, Is.EqualTo(0));
    }

    // ItemSocketScroll 0x008B mode 1: u8 mode, i64 target uid, i64 scroll uid.
    [Test]
    public void SocketScrollPacket() {
        var writer = new ByteWriter();
        writer.WriteByte(1);
        writer.WriteLong(222);
        writer.WriteLong(111);
        var packet = new ByteReader(writer.ToArray(), 0);
        Assert.That(packet.ReadByte(), Is.EqualTo(1));
        Assert.That(packet.ReadLong(), Is.EqualTo(222));
        Assert.That(packet.ReadLong(), Is.EqualTo(111));
        Assert.That(packet.Available, Is.EqualTo(0));
    }

    // ItemExtractionScroll 0x00A0: i64 scroll uid, i64 source uid, no mode byte.
    [Test]
    public void ExtractionPacket() {
        var writer = new ByteWriter();
        writer.WriteLong(111);
        writer.WriteLong(222);
        var packet = new ByteReader(writer.ToArray(), 0);
        Assert.That(packet.ReadLong(), Is.EqualTo(111));
        Assert.That(packet.ReadLong(), Is.EqualTo(222));
        Assert.That(packet.Available, Is.EqualTo(0));
    }

    [TestCase(50, 50, 60, true)]
    [TestCase(60, 50, 60, true)]
    [TestCase(49, 50, 60, false)] // below: the old test let this through
    [TestCase(61, 50, 60, false)] // above
    public void RepackLevel(int level, int min, int max, bool expected) {
        Assert.That(ScrollRules.RepackLevelAllowed(level, min, max), Is.EqualTo(expected));
    }

    [TestCase(70, 50, 0, true)] // no upper bound
    [TestCase(49, 50, 0, false)]
    [TestCase(61, 50, 60, false)]
    [TestCase(60, 50, 60, true)]
    public void SocketLevel(int level, int min, int max, bool expected) {
        Assert.That(ScrollRules.SocketLevelAllowed(level, min, max), Is.EqualTo(expected));
    }

    [TestCase(1, 0, true)]
    [TestCase(1, 1, false)] // the last repack is used up
    [TestCase(0, 0, false)] // not a repackable item
    [TestCase(2, 3, false)]
    public void RepackLeft(int limit, int used, bool expected) {
        Assert.That(ScrollRules.HasRepackLeft(limit, used), Is.EqualTo(expected));
    }

    [Test]
    public void RepackScrollList() {
        Assert.That(ScrollRules.RepackScrollAllowed([], 7), Is.True);
        Assert.That(ScrollRules.RepackScrollAllowed([3, 7], 7), Is.True);
        Assert.That(ScrollRules.RepackScrollAllowed([3], 7), Is.False);
    }

    [TestCase(false, false, true)] // no enchant block
    [TestCase(true, true, true)]
    [TestCase(true, false, false)]
    public void RepackEnchant(bool hasEnchant, bool enabled, bool expected) {
        Assert.That(ScrollRules.RepackEnchantAllowed(hasEnchant, enabled), Is.EqualTo(expected));
    }

    [TestCase(3, true)]
    [TestCase(5, true)]
    [TestCase(0, false)]
    [TestCase(1, false)]
    [TestCase(2, false)]
    [TestCase(4, false)]
    [TestCase(6, false)]
    [TestCase(7, false)]
    public void RepackTransferType(int transferType, bool expected) {
        Assert.That(ScrollRules.RepackTransferTypeAllowed(transferType), Is.EqualTo(expected));
    }

    [Test]
    public void RepackSlot() {
        Assert.That(ScrollRules.RepackSlotAllowed(false, false, [12, 13], 13), Is.True);
        Assert.That(ScrollRules.RepackSlotAllowed(false, false, [12, 13], 20), Is.False);
        Assert.That(ScrollRules.RepackSlotAllowed(true, true, [], 0), Is.True);
        Assert.That(ScrollRules.RepackSlotAllowed(true, false, [12], 12), Is.False);
    }

    [TestCase(1, 0, true)] // a repack always costs a scroll
    [TestCase(0, 0, false)]
    [TestCase(2, 3, false)]
    [TestCase(3, 3, true)]
    public void RepackScrolls(int have, int need, bool expected) {
        Assert.That(ScrollRules.HasRepackScrolls(have, need), Is.EqualTo(expected));
    }
}
