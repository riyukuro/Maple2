using Maple2.Server.Core.Formulas;

namespace Maple2.Server.Tests.Core;

public class HitRateTests {
    [Test]
    public void LuckAddsToTheHitRate() {
        // Accuracy 82 against evasion 82: 0.9756 without luck.
        Assert.That(Damage.HitRate(82, 82, 0), Is.EqualTo(0.9756).Within(0.0001));
        Assert.That(Damage.HitRate(82, 82, 18), Is.EqualTo(0.9756 + 0.0132).Within(0.0001));
        Assert.That(Damage.HitRate(82, 97, 18), Is.EqualTo(0.8856 + 0.0132).Within(0.0001));
        Assert.That(Damage.HitRate(82, 87, 26), Is.EqualTo(0.9436 + 0.0171).Within(0.0001));
    }

    [Test]
    public void LuckTermIsCapped() {
        Assert.That(Damage.HitRate(82, 82, 1000000) - Damage.HitRate(82, 82, 0), Is.EqualTo(0.05).Within(0.0001));
        Assert.That(Damage.HitRate(82, 82, -5), Is.EqualTo(Damage.HitRate(82, 82, 0)));
    }

    // No npc has a luck stat (0 of the 8,183 npc rows), so an npc's hit on a player takes this path.
    [Test]
    public void NpcWithoutLuckIsUnchanged() {
        Assert.That(Damage.HitRate(80, 70, 0), Is.EqualTo((80 - 10) / (70 + 0.8 * 80) * 2).Within(1e-9));
    }
}
