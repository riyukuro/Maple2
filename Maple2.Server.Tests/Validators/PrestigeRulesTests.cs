using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Tests.Validators;

public class PrestigeRulesTests {
    // Prestige 0x00AC mode 5: u8 mode, i32 mission id.
    [TestCase(false, 9L, 10, false)]
    [TestCase(false, 10L, 10, true)]
    [TestCase(false, 25L, 10, true)]
    [TestCase(true, 10L, 10, false)]
    [TestCase(false, 0L, 10, false)]
    public void MissionRewardClaim(bool awarded, long gainedLevels, int requiredCount, bool expected) {
        var packet = new ByteReader(new byte[] { 0x05, 0x03, 0x00, 0x00, 0x00 }, 0);
        Assert.That(packet.ReadByte(), Is.EqualTo(5));
        Assert.That(packet.ReadInt(), Is.EqualTo(3));
        Assert.That(packet.Available, Is.EqualTo(0));
        Assert.That(PrestigeRules.CanClaimMission(awarded, gainedLevels, requiredCount), Is.EqualTo(expected));
    }
}
