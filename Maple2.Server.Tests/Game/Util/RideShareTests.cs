using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

public class RideShareTests {
    [Test]
    public void FreeSeat_IsFirstEmpty() {
        Assert.That(RideShare.Seat(false, false, false, new[] { 5, 0, 0 }), Is.EqualTo(1));
    }

    [Test]
    public void FullMount_HasNoSeat() {
        Assert.That(RideShare.Seat(false, false, false, new[] { 5, 6 }), Is.EqualTo(-1));
    }

    [Test]
    public void SingleSeatMount_HasNoSeat() {
        Assert.That(RideShare.Seat(false, false, false, new int[0]), Is.EqualTo(-1));
    }

    [Test]
    public void DeadJoiner_HasNoSeat() {
        Assert.That(RideShare.Seat(true, false, false, new[] { 0 }), Is.EqualTo(-1));
    }

    [Test]
    public void DeadOwner_HasNoSeat() {
        Assert.That(RideShare.Seat(false, true, false, new[] { 0 }), Is.EqualTo(-1));
    }

    [Test]
    public void OwnerRidingAlone_HasNoSeat() {
        Assert.That(RideShare.Seat(false, false, true, new[] { 0 }), Is.EqualTo(-1));
    }
}
