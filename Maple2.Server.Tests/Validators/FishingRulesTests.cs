using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Tests.Validators;

public class FishingRulesTests {
    // Fishing 0x0073 mode 8 after the mode byte: u8 result.
    [TestCase((byte) 1, true, 20000, 15000, true)] // bite, tick passed
    [TestCase((byte) 1, true, 14500, 15000, true)] // inside the slack
    [TestCase((byte) 1, true, 10001, 15000, false)] // sent right after the start
    [TestCase((byte) 1, false, 40000, 15000, false)] // the cast had no bite
    [TestCase((byte) 0, true, 20000, 15000, false)] // the client reports a miss
    public void ResultByteOfCatch(byte wire, bool bite, long nowTick, long biteTick, bool expected) {
        var packet = new ByteReader(new byte[] { wire }, 0);
        bool success = packet.ReadBool();
        Assert.That(packet.Available, Is.EqualTo(0));
        Assert.That(FishingRules.IsCatch(success, bite, nowTick, biteTick), Is.EqualTo(expected));
    }
}
