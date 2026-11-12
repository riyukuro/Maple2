using Maple2.Model.Enum;
using Maple2.Server.Game.Model.Skill;

namespace Maple2.Server.Tests.Game.Util;

// The critical chance of a heal: the caster's critical rate against the target's critical evasion. KMS2 heals of a
// priest on itself crit at these rates (42 of 566, 35 of 554, 8 of 230 and 8 of 24 heals).
public class HealCritTests {
    [TestCase(18, 53, 50, 0.062f)]
    [TestCase(18, 60, 50, 0.068f)]
    [TestCase(18, 53, 62, 0.050f)]
    [TestCase(18, 305, 50, 0.262f)]
    public void PriestHealCritChance(long luck, long criticalRate, long targetCriticalEvasion, float expected) {
        float chance = global::Maple2.Server.Game.LuaFunctions.Lua.CalcPlayerCritRate((int) JobCode.Priest, luck, criticalRate, targetCriticalEvasion, 0, 0);
        Assert.That(chance, Is.EqualTo(expected).Within(0.001f));
    }

    // Without the target's evasion the rate ran to the cap, 40 % for every caster.
    [Test]
    public void NoEvasionIsTheCap() {
        Assert.That(global::Maple2.Server.Game.LuaFunctions.Lua.CalcPlayerCritRate((int) JobCode.Priest, 18, 53, 0, 0, 0), Is.EqualTo(0.4f));
    }

    // KMS2 heals of five priests on themselves: the recovery bonus is 0, 5 (an earring's option), 23 (a codex's
    // option) and 50 (a codex's 10 and the passive 10400141 at level 1, 0.04).
    [TestCase(4.16f, 251, 0, false, 1044)]
    [TestCase(4.8f, 251, 0, false, 1204)]
    [TestCase(0.9f, 272, 5, false, 245)]
    [TestCase(0.9f, 272, 5, true, 367)]
    [TestCase(3.52f, 275, 5, false, 972)]
    [TestCase(3.52f, 275, 5, true, 1458)]
    [TestCase(3.52f, 286, 5, false, 1011)]
    [TestCase(1.23f, 289, 5, false, 356)]
    [TestCase(0.9f, 187, 23, false, 171)]
    [TestCase(1.09f, 838, 50, false, 958)]
    [TestCase(1.09f, 838, 50, true, 1437)]
    [TestCase(2f, 738, 50, false, 1549)]
    [TestCase(2f, 738, 50, true, 2323)]
    [TestCase(6.08f, 809, 50, false, 5163)]
    [TestCase(0.9f, 738, 50, false, 697)]
    [TestCase(0.9f, 738, 50, true, 1045)]
    public void RecoveryBonusRaisesTheCasterPart(float recoveryRate, long magicalAtk, long recoveryBonus, bool crit, long expected) {
        Assert.That(HealDamageRecord.CasterPart(recoveryRate, magicalAtk, recoveryBonus, crit), Is.EqualTo(expected));
    }

    [Test]
    public void CritHealsForHalfMore() {
        // KMS2: a heal of 972 crits for 1,458.
        Assert.That((int) (972 * HealDamageRecord.CritMultiplier), Is.EqualTo(1458));
    }
}
