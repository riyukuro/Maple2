using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Validators;

namespace Maple2.Server.Tests.Validators;

public class InteriorPointRulesTests {
    // The counter is the best score, not the sum: 600 reported twice stays 600.
    [TestCase(0, 600, 600, true)]
    [TestCase(600, 600, 600, false)]
    [TestCase(600, 300, 600, false)]
    [TestCase(600, 1100, 1100, true)]
    public void Counter_KeepsTheHighestScore(long counter, long score, long expected, bool advances) {
        Assert.That(InteriorPointRules.Counter(counter, score), Is.EqualTo(expected));
        Assert.That(InteriorPointRules.Advances(counter, score), Is.EqualTo(advances));
    }

    // One more cube moves the score by the points of the goal it completes: 50, 100 or 200.
    [Test]
    public void Score_AddsThePointsOfEachGoalMet() {
        var cubes = new Dictionary<HousingCategory, int>();
        Assert.That(InteriorPointRules.Score(cubes), Is.EqualTo(0));
        cubes[HousingCategory.Bed] = 1;
        Assert.That(InteriorPointRules.Score(cubes), Is.EqualTo(100));
        cubes[HousingCategory.Table] = 1;
        Assert.That(InteriorPointRules.Score(cubes), Is.EqualTo(150));
        cubes[HousingCategory.SofasChairs] = 1; // the goal is 2
        Assert.That(InteriorPointRules.Score(cubes), Is.EqualTo(150));
        cubes[HousingCategory.SofasChairs] = 2;
        Assert.That(InteriorPointRules.Score(cubes), Is.EqualTo(250));
        cubes[HousingCategory.Electronics] = 1;
        Assert.That(InteriorPointRules.Score(cubes), Is.EqualTo(450));
    }

    [Test]
    public void Score_IsElevenHundredWithEveryGoalMet() {
        var cubes = System.Enum.GetValues<HousingCategory>().ToDictionary(category => category, _ => 4);
        Assert.That(InteriorPointRules.Score(cubes), Is.EqualTo(1100));
    }
}
