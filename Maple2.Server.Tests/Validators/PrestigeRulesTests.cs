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

    // Made-up ability: from level 5, one more step every 50 levels, 20 steps at most.
    [TestCase(1, 5, 50, 20, 4, 0)]
    [TestCase(1, 5, 50, 20, 5, 1)]
    [TestCase(1, 5, 50, 20, 54, 1)]
    [TestCase(1, 5, 50, 20, 55, 2)]
    [TestCase(1, 5, 50, 20, 5000, 20)]
    [TestCase(1, 30, 0, 1, 29, 0)]
    [TestCase(1, 30, 0, 1, 900, 1)]
    [TestCase(0, 5, 50, 20, 100, 0)]
    public void AbilityStep_AsTheClientCountsIt(int id, int requiredLevel, int interval, int maxCount, int prestigeLevel, int expected) {
        Assert.That(PrestigeRules.AbilityStep(id, requiredLevel, interval, maxCount, prestigeLevel), Is.EqualTo((short) expected));
    }

    [TestCase(999L, 1000L, 7, 100, 0)]
    [TestCase(1000L, 1000L, 7, 100, 1)]
    [TestCase(3500L, 1000L, 7, 100, 3)]
    [TestCase(3500L, 1000L, 99, 100, 1)]
    [TestCase(3500L, 1000L, 100, 100, 0)]
    public void LevelsGained_EachLevelOnce(long exp, long expPerLevel, int level, int limit, int expected) {
        Assert.That(PrestigeRules.LevelsGained(exp, expPerLevel, level, limit), Is.EqualTo(expected));
    }
}
