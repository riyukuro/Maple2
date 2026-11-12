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

    [Test]
    public void CritHealsForHalfMore() {
        // KMS2: a heal of 972 crits for 1,458.
        Assert.That((int) (972 * HealDamageRecord.CritMultiplier), Is.EqualTo(1458));
    }
}
