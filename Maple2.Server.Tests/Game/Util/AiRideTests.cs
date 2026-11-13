using System;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

public class AiRideTests {
    // AI_Blackbean_Phase01_Normal/Hard: rideNpcIDs="23000097, 23000098, 23000105, 23000106".
    private static readonly int[] BlackbeanCars = [23000097, 23000098, 23000105, 23000106];

    [Test]
    public void TheBossRidesItsFirstLiveCar() {
        // Other slaves (mini Blackbeans) come before the car: the car is still the one ridden.
        Assert.That(AiRide.Vehicle([21000001, 23000098, 23000097], BlackbeanCars), Is.EqualTo(1));
        Assert.That(AiRide.Vehicle([23000105], BlackbeanCars), Is.EqualTo(0));
        // No car alive: nothing to ride.
        Assert.That(AiRide.Vehicle([21000001], BlackbeanCars), Is.EqualTo(-1));
        Assert.That(AiRide.Vehicle([], BlackbeanCars), Is.EqualTo(-1));
    }

    [Test]
    public void RideOffClearsTheRide() {
        Assert.That(AiRide.Riding(false, BlackbeanCars), Is.EqualTo(BlackbeanCars));
        // <node name="ride" type="slave" isRideOff="true" /> carries no ids.
        Assert.That(AiRide.Riding(true, []), Is.Null);
        Assert.That(AiRide.Riding(true, BlackbeanCars), Is.Null);
        Assert.That(AiRide.Riding(false, Array.Empty<int>()), Is.Null);
    }
}
