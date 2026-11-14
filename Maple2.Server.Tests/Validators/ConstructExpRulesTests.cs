using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Validators;

public class ConstructExpRulesTests {
    [TestCase(0, ConstructExpRules.Step.Exp)]
    [TestCase(99, ConstructExpRules.Step.Exp)]
    [TestCase(100, ConstructExpRules.Step.OverTime)]
    [TestCase(101, ConstructExpRules.Step.None)]
    public void Check_PaysUpToTheDailyCapThenNoticesOnce(int paidToday, ConstructExpRules.Step expected) {
        Assert.That(ConstructExpRules.Check(hasRow: true, paidToday, maxPerDay: 100), Is.EqualTo(expected));
    }

    [Test]
    public void Check_NoConstructRow_NothingAndNoNotice() {
        // The ingest drops the row when the feature ConstructExp is off.
        Assert.That(ConstructExpRules.Check(hasRow: false, 0, 100), Is.EqualTo(ConstructExpRules.Step.None));
        Assert.That(ConstructExpRules.Check(hasRow: false, 100, 100), Is.EqualTo(ConstructExpRules.Step.None));
    }

    [Test]
    public void Check_NoCap_Nothing() {
        Assert.That(ConstructExpRules.Check(hasRow: true, 0, 0), Is.EqualTo(ConstructExpRules.Step.None));
    }
}
