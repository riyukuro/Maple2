using Maple2.Model.Validators;

namespace Maple2.Server.Tests.Validators;

public class UgcMarketRulesTests {
    // The share of each step of the table KMS2 sends, at the ends of the ranges the tooltip lists.
    [TestCase(0, 70)]
    [TestCase(1, 70)]
    [TestCase(5000, 70)]
    [TestCase(5001, 60)]
    [TestCase(10000, 60)]
    [TestCase(10001, 50)]
    [TestCase(100000, 50)]
    [TestCase(100001, 40)]
    [TestCase(200000, 40)]
    [TestCase(200001, 20)]
    [TestCase(long.MaxValue, 20)]
    public void SharePercent_ByStep(long salesTotal, int expected) {
        Assert.That(UgcMarketRules.SharePercent(UgcMarketRules.CostRows, salesTotal), Is.EqualTo(expected));
    }

    // KMS2: a design sold at 300 gave its seller 210, on the first sale and at a total of 300.
    [TestCase(0, 300, 210)]
    [TestCase(300, 300, 210)]
    [TestCase(5001, 300, 180)]
    [TestCase(10001, 300, 150)]
    [TestCase(100001, 300, 120)]
    [TestCase(200001, 300, 60)]
    // Rounded down to whole numbers.
    [TestCase(0, 333, 233)]
    [TestCase(5001, 333, 199)]
    [TestCase(200001, 333, 66)]
    [TestCase(0, 1, 0)]
    [TestCase(0, 0, 0)]
    [TestCase(0, -5, 0)]
    public void Profit_ByStep(long salesTotal, long price, long expected) {
        Assert.That(UgcMarketRules.Profit(UgcMarketRules.CostRows, salesTotal, price), Is.EqualTo(expected));
    }

    [Test]
    public void NoRows_WholePrice() {
        Assert.That(UgcMarketRules.Profit([], 123456, 300), Is.EqualTo(300));
    }
}
