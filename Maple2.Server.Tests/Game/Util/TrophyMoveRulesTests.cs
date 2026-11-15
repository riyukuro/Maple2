using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

public class TrophyMoveRulesTests {
    [Test]
    public void Counts_WaitForFiveAndKeepTheRest() {
        float acc = 0;
        Assert.That(TrophyMoveRules.Counts(ref acc, 250f), Is.EqualTo(0));
        Assert.That(TrophyMoveRules.Counts(ref acc, 240f), Is.EqualTo(0));
        Assert.That(acc, Is.EqualTo(490f));
        // 520 units: five counts, 20 kept.
        Assert.That(TrophyMoveRules.Counts(ref acc, 30f), Is.EqualTo(5));
        Assert.That(acc, Is.EqualTo(20f).Within(0.001f));
        // A long move reports every whole count at once.
        Assert.That(TrophyMoveRules.Counts(ref acc, 790f), Is.EqualTo(8));
        Assert.That(acc, Is.EqualTo(10f).Within(0.001f));
    }

    [Test]
    public void Counts_IgnoreNoMove() {
        float acc = 450f;
        Assert.That(TrophyMoveRules.Counts(ref acc, 0f), Is.EqualTo(0));
        Assert.That(TrophyMoveRules.Counts(ref acc, -10f), Is.EqualTo(0));
        Assert.That(TrophyMoveRules.Counts(ref acc, float.NaN), Is.EqualTo(0));
        Assert.That(acc, Is.EqualTo(450f));
    }
}
