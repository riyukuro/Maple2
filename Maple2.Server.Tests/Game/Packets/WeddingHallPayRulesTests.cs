using Maple2.Model.Game;

namespace Maple2.Server.Tests.Game.Packets;

public class WeddingHallPayRulesTests {
    [Test]
    public void FirstReservationPaysTheHallAndMailsGifts() {
        Assert.That(WeddingHallPayRules.Pay(change: false, coupon: false, hallCost: 500, difference: 0), Is.EqualTo((500L, true)));
        Assert.That(WeddingHallPayRules.Pay(change: false, coupon: true, hallCost: 500, difference: 0), Is.EqualTo((0L, false)));
    }

    [Test]
    public void ChangePaysOnlyTheDifference() {
        // A change of the time alone keeps the hall: difference 0 costs nothing, not the hall price.
        Assert.That(WeddingHallPayRules.Pay(change: true, coupon: false, hallCost: 500, difference: 0), Is.EqualTo((0L, false)));
        Assert.That(WeddingHallPayRules.Pay(change: true, coupon: false, hallCost: 900, difference: 400), Is.EqualTo((400L, false)));
        Assert.That(WeddingHallPayRules.Pay(change: true, coupon: true, hallCost: 900, difference: 400), Is.EqualTo((400L, false)));
    }
}
