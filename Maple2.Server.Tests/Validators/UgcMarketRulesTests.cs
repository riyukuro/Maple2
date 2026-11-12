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

    // calcMarketRegisterFee: nothing for the first listing, then by the listings already on sale.
    [TestCase(0, false, false, false, 0)]
    [TestCase(1, false, false, false, 30)]
    [TestCase(2, false, false, false, 50)]
    [TestCase(8, false, false, false, 50)]
    [TestCase(9, false, false, false, 100)]
    [TestCase(40, false, false, false, 100)]
    [TestCase(0, false, false, true, 0)]
    [TestCase(1, false, false, true, 120)]
    [TestCase(2, false, false, true, 200)]
    [TestCase(8, false, false, true, 200)]
    [TestCase(9, false, false, true, 300)]
    // A music score has no register fee; the promotion fee is added for both.
    [TestCase(9, true, false, false, 0)]
    [TestCase(9, true, true, false, 100)]
    [TestCase(0, false, true, false, 100)]
    [TestCase(1, false, true, false, 130)]
    [TestCase(-3, false, false, false, 0)]
    public void RegisterFee_ByListings(int activeCount, bool score, bool promote, bool cn, long expected) {
        Assert.That(UgcMarketRules.RegisterFee(activeCount, score, promote, 100, cn), Is.EqualTo(expected));
    }

    [Test]
    public void RegisterFee_NeverBelowZero() {
        Assert.That(UgcMarketRules.RegisterFee(0, false, true, -500, false), Is.EqualTo(0));
    }

    [TestCase(100, 10000, 0, 0, false, false, 100, 10000)]
    [TestCase(100, 10000, 300, 5000, false, false, 300, 5000)]
    [TestCase(100, 10000, 300, 0, false, false, 300, 10000)]
    [TestCase(100, 10000, 0, 5000, false, false, 100, 5000)]
    // A music score: the script's lowest price, the constant's highest; its design row is not read.
    [TestCase(50, 10000, 300, 5000, true, false, 100, 10000)]
    [TestCase(50, 10000, 0, 0, true, true, 15, 10000)]
    // Never below 0, and the highest at least the lowest.
    [TestCase(-20, 10000, 0, 0, false, false, 0, 10000)]
    [TestCase(500, 100, 0, 0, false, false, 500, 500)]
    public void PriceRange_ByItem(long constantMin, long constantMax, long designMin, long designMax, bool score, bool cn, long min, long max) {
        Assert.That(UgcMarketRules.PriceRange(constantMin, constantMax, designMin, designMax, score, cn), Is.EqualTo((min, max)));
    }

    [TestCase(-500, 100)]
    [TestCase(0, 100)]
    [TestCase(100, 100)]
    [TestCase(4000, 4000)]
    [TestCase(long.MaxValue, 10000)]
    public void ClampPrice_IntoRange(long price, long expected) {
        Assert.That(UgcMarketRules.ClampPrice((100, 10000), price), Is.EqualTo(expected));
    }

    [TestCase(true, 2000, 1000, 300, true)]
    [TestCase(true, 2000, 1000, 0, true)]
    // Taken off sale, sale time over, or a price that would pay the buyer.
    [TestCase(false, 2000, 1000, 300, false)]
    [TestCase(true, 0, 1000, 300, false)]
    [TestCase(true, 1000, 1000, 300, false)]
    [TestCase(true, 2000, 1000, -300, false)]
    public void CanBuy_OnSaleOnly(bool active, long endTime, long now, long price, bool expected) {
        Assert.That(UgcMarketRules.CanBuy(active, endTime, now, price), Is.EqualTo(expected));
    }
}
