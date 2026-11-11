using Maple2.Model.Validators;

namespace Maple2.Server.Tests.Validators;

public class TaxiRulesTests {
    [TestCase(0, true)]
    [TestCase(5, true)]
    [TestCase(1, false)]
    [TestCase(3, false)]
    public void DiscoverModes(byte mode, bool expected) {
        Assert.That(TaxiRules.IsDiscover(mode), Is.EqualTo(expected));
    }

    [TestCase(false, true, true)]
    [TestCase(true, true, false)]
    [TestCase(false, false, false)]
    public void Discover(bool known, bool station, bool expected) {
        Assert.That(TaxiRules.CanDiscover(known, station), Is.EqualTo(expected));
    }
}
