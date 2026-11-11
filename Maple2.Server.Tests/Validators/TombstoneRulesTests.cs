using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Tests.Validators;

public class TombstoneRulesTests {
    // Tombstone 0x002A: i32 object id, i32 hits. The hitter here has object id 7.
    [TestCase(9, 1, true)]
    [TestCase(7, 1, false)] // the own tombstone
    [TestCase(9, 0, false)]
    [TestCase(9, 2, false)]
    [TestCase(9, -1, false)]
    [TestCase(9, 255, false)]
    [TestCase(9, 257, false)] // 1 after a cut to a byte
    public void HitPacket(int objectId, int hits, bool expected) {
        var writer = new ByteWriter();
        writer.WriteInt(objectId);
        writer.WriteInt(hits);
        var packet = new ByteReader(writer.ToArray(), 0);
        int owner = packet.ReadInt();
        Assert.That(TombstoneRules.IsHit(packet.ReadInt(), owner, 7), Is.EqualTo(expected));
        Assert.That(packet.Available, Is.EqualTo(0));
    }

    [TestCase((byte) 3, (byte) 2)]
    [TestCase((byte) 1, (byte) 0)]
    [TestCase((byte) 0, (byte) 0)]
    public void OneHitLess(byte remaining, byte expected) {
        Assert.That(TombstoneRules.AfterHit(remaining), Is.EqualTo(expected));
    }
}
