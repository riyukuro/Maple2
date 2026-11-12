using Maple2.Server.World.Containers;

namespace Maple2.Server.Tests.World;

public class PlayerConfigSettleTests {
    // A recharge skill's cooldown while its holder was away: each ended cooldown gives a use back and the next one
    // runs its length again; with the last use back the cooldown is over (remaining under 0).
    [TestCase(5000, 3, 10000, 5000, 3)]
    [TestCase(-2000, 3, 10000, 8000, 2)]
    [TestCase(-12000, 3, 10000, 8000, 1)]
    [TestCase(-22000, 3, 10000, -2000, 1)]
    [TestCase(-2000, 1, 10000, -2000, 1)]
    [TestCase(-2000, 3, 0, -2000, 3)]
    public void SettleRecharge_OneUseBackPerEndedCooldown(long remaining, int charges, long rechargeMs, long expectedRemaining, int expectedCharges) {
        Assert.That(PlayerConfigLookUp.SettleRecharge(remaining, charges, rechargeMs), Is.EqualTo((expectedRemaining, expectedCharges)));
    }
}
