using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

public class AiSelectTests {
    // A select with useNpcProb="1" over three skill branches; the npc has two skills weighted 80 and 20.
    [Test]
    public void UseNpcProbTakesTheNpcSkillProbs() {
        int[] weights = AiSelect.Weights([100], true, [80, 20], 3);
        Assert.That(weights, Is.EqualTo(new[] { 80, 20, 0 }));
    }

    [Test]
    public void UseNpcProbWithMoreSkillsThanBranchesStopsAtTheBranches() {
        int[] weights = AiSelect.Weights([100], true, [10, 20, 30, 40], 3);
        Assert.That(weights, Is.EqualTo(new[] { 10, 20, 30 }));
    }

    // Data ingested without the npc probs keeps the node's own list.
    [Test]
    public void UseNpcProbWithoutNpcProbsFallsBackToTheNodeProb() {
        int[] weights = AiSelect.Weights([100], true, [0, 0], 3);
        Assert.That(weights, Is.EqualTo(new[] { 100, 0, 0 }));
    }

    [Test]
    public void PlainSelectUsesItsOwnProb() {
        int[] weights = AiSelect.Weights([70, 30, 0], false, [5, 95], 3);
        Assert.That(weights, Is.EqualTo(new[] { 70, 30, 0 }));
    }

    [Test]
    public void ShortProbListLeavesTheRestAtZero() {
        int[] weights = AiSelect.Weights([60], false, [], 2);
        Assert.That(weights, Is.EqualTo(new[] { 60, 0 }));
    }
}
