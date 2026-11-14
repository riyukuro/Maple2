using Maple2.Model.Enum;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game;

// A player's crit rate as the client's CLua::FUN_1406747a0(job, luk, critRate, res, bonus, rate) computes it:
// clamp((luk x factor + critRate x 5.3) x 0.015 / (res x 2), 0, 0.4), then the special 202 (bonus x 5.3 x 0.015 /
// (res x 2)) and the special 205 (rate / 1000) past the clamp; a resistance of 0 or less counts as 1, and a job code
// with no factor (None, GM) has 0.
public class CriticalRateTests {
    private static float Rate(JobCode job, long luk, long critRate, long res, int bonus, int rate) =>
        Maple2.Server.Game.LuaFunctions.Lua.CalcPlayerCritRate((int) job, luk, critRate, res, bonus, rate);

    // Knight, luck 100, crit rate 50, resistance 50: (378 + 265) x 0.015 / 100.
    [Test]
    public void Plain() {
        Assert.That(Rate(JobCode.Knight, 100, 50, 50, 0, 0), Is.EqualTo(0.09645f).Within(1e-6f));
    }

    // The same with special 202 at 20 (+0.0159) and special 205 at 10 (+0.01).
    [Test]
    public void SpecialsAreAdded() {
        Assert.That(Rate(JobCode.Knight, 100, 50, 50, 20, 10), Is.EqualTo(0.12235f).Within(1e-6f));
    }

    // The specials go past the 0.4 clamp: luck 1000, crit rate 1000 clamps to 0.4, special 205 at 100 adds 0.1.
    [Test]
    public void SpecialsPassTheClamp() {
        Assert.That(Rate(JobCode.Knight, 1000, 1000, 50, 0, 100), Is.EqualTo(0.5f).Within(1e-6f));
    }

    // A resistance of -10 counts as 1: (378 + 265) x 0.015 / 2 clamps to 0.4 (the old division by -20 gave 0).
    [Test]
    public void NegativeResistanceCountsAsOne() {
        Assert.That(Rate(JobCode.Knight, 100, 50, -10, 0, 0), Is.EqualTo(0.4f));
    }

    // Job None has no luck factor: 265 x 0.015 / 100 (the old 0.7 factor gave 0.05025).
    [Test]
    public void JobNoneHasNoLuckFactor() {
        Assert.That(Rate(JobCode.None, 100, 50, 50, 0, 0), Is.EqualTo(0.03975f).Within(1e-6f));
    }
}
