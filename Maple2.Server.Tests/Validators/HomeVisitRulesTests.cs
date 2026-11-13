using Maple2.Model.Validators;

namespace Maple2.Server.Tests.Validators;

public class HomeVisitRulesTests {
    [Test]
    public void DeadComesFirst() {
        Assert.That(HomeVisitRules.Check(isDead: true, homeReturnable: false, inUgcMap: false, inOwnHome: true),
            Is.EqualTo(HomeVisitRules.Refusal.Dead));
    }

    [Test]
    public void MapWithoutHomeReturnable_IsRefusedOutsideAHome() {
        Assert.That(HomeVisitRules.Check(false, homeReturnable: false, inUgcMap: false, inOwnHome: false),
            Is.EqualTo(HomeVisitRules.Refusal.DisabledMap));
        Assert.That(HomeVisitRules.Check(false, homeReturnable: false, inUgcMap: true, inOwnHome: false),
            Is.EqualTo(HomeVisitRules.Refusal.None));
    }

    [Test]
    public void OwnHome_IsRefusedOnlyFromInsideIt() {
        Assert.That(HomeVisitRules.Check(false, homeReturnable: true, inUgcMap: true, inOwnHome: true),
            Is.EqualTo(HomeVisitRules.Refusal.MyHome));
        // The way home from a town names the player's own account too.
        Assert.That(HomeVisitRules.Check(false, homeReturnable: true, inUgcMap: false, inOwnHome: false),
            Is.EqualTo(HomeVisitRules.Refusal.None));
        Assert.That(HomeVisitRules.Check(false, homeReturnable: true, inUgcMap: true, inOwnHome: false),
            Is.EqualTo(HomeVisitRules.Refusal.None));
    }
}
