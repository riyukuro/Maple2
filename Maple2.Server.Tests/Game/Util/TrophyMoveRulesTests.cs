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

    [Test]
    public void Counts_NoOverflowOnAHugeSum() {
        float acc = 0;
        Assert.That(TrophyMoveRules.Counts(ref acc, 1e12f), Is.EqualTo(int.MaxValue));
        Assert.That(acc, Is.LessThan(1e12f));
    }

    [Test]
    public void Budget_CountsHonestMovesInFull() {
        var budget = new TrophyMoveBudget();
        // Walking speed at the highest move rate, about 600 units a second, synced every 200 ms for a minute.
        float counted = 0;
        for (long now = 0; now <= 60_000; now += 200) {
            counted += budget.Take(120f, now, MoveRules.MaxDistance);
        }
        Assert.That(counted, Is.EqualTo(301 * 120f).Within(0.5f));
        // Two seconds of positions held up and sent together still count.
        Assert.That(budget.Take(1800f, 62_000, MoveRules.MaxDistance), Is.EqualTo(1800f));
        Assert.That(budget.Take(1800f, 62_000, MoveRules.MaxDistance), Is.EqualTo(1800f).Within(0.5f));
    }

    [Test]
    public void Budget_CapsAMoveFurtherThanTheFastest() {
        var budget = new TrophyMoveBudget();
        Assert.That(budget.Take(100_000f, 0, MoveRules.MaxDistance), Is.EqualTo(3600f));
        // Nothing is left; 100 ms later a tenth of a second's move counts.
        Assert.That(budget.Take(100_000f, 100, MoveRules.MaxDistance), Is.EqualTo(180f).Within(0.01f));
        Assert.That(budget.Take(float.PositiveInfinity, 5000, MoveRules.MaxDistance), Is.EqualTo(0f));
    }

    [Test]
    public void Budget_SkipsTheServersMove() {
        var budget = new TrophyMoveBudget();
        budget.ServerMoved(1000);
        Assert.That(budget.Take(5000f, 1500, MoveRules.MaxDistance), Is.EqualTo(0f));
        Assert.That(budget.Take(300f, 1000 + MoveRules.ServerMoveGraceMs - 1, MoveRules.MaxDistance), Is.EqualTo(0f));
        Assert.That(budget.Take(300f, 1000 + MoveRules.ServerMoveGraceMs, MoveRules.MaxDistance), Is.EqualTo(300f));
    }
}
