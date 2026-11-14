using Maple2.Model.Enum;
using Maple2.Server.Game.PacketHandlers;

namespace Maple2.Server.Tests.Game;

// Go home (RequestHome Warp): allowed from inside any home that is not the player's own, even though every
// home field has homeReturnable 0; refused for a dead player; refused in the own home and in other fields
// without the flag.
public class HomeWarpTests {
    private const long Me = 1001;
    private const long Friend = 2002;

    [Test]
    public void FromAFriendsHome_Goes() {
        Assert.That(HomeHandler.WarpRefusal(false, false, Friend, Me), Is.Null);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void FromTheOwnHome_SamePlace(bool homeReturnable) {
        Assert.That(HomeHandler.WarpRefusal(false, homeReturnable, Me, Me), Is.EqualTo(StringCode.s_home_returnable_forbidden_to_sameplace));
    }

    [Test]
    public void FromAReturnableField_Goes() {
        Assert.That(HomeHandler.WarpRefusal(false, true, null, Me), Is.Null);
    }

    [Test]
    public void FromAFieldWithoutTheFlag_Forbidden() {
        Assert.That(HomeHandler.WarpRefusal(false, false, null, Me), Is.EqualTo(StringCode.s_home_returnable_forbidden));
    }

    [TestCase(true, null)]
    [TestCase(false, Friend)]
    public void Dead_InvalidState(bool homeReturnable, long? homeOwnerId) {
        Assert.That(HomeHandler.WarpRefusal(true, homeReturnable, homeOwnerId, Me), Is.EqualTo(StringCode.s_home_returnable_invalid_state));
    }
}
