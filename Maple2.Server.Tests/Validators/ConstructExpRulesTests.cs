using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Validators;

public class ConstructExpRulesTests {
    [TestCase(0, ConstructExpRules.Step.Exp)]
    [TestCase(99, ConstructExpRules.Step.Exp)]
    [TestCase(100, ConstructExpRules.Step.OverTime)]
    [TestCase(101, ConstructExpRules.Step.None)]
    public void Check_PaysUpToTheDailyCapThenNoticesOnce(int paidToday, ConstructExpRules.Step expected) {
        Assert.That(ConstructExpRules.Check(enabled: true, paidToday, maxPerDay: 100), Is.EqualTo(expected));
    }

    [Test]
    public void Check_NotEnabled_NothingAndNoNotice() {
        // The feature ConstructExp is off in the feature server table, or the common exp table has no construct row.
        Assert.That(ConstructExpRules.Check(enabled: false, 0, 100), Is.EqualTo(ConstructExpRules.Step.None));
        Assert.That(ConstructExpRules.Check(enabled: false, 100, 100), Is.EqualTo(ConstructExpRules.Step.None));
    }

    [Test]
    public void Check_NoCap_Nothing() {
        Assert.That(ConstructExpRules.Check(enabled: true, 0, 0), Is.EqualTo(ConstructExpRules.Step.None));
    }
}
