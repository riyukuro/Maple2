using Maple2.Model.Enum;
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

    [Test]
    public void ClearSeats_ReturnsThePassengers_EmptiesTheSeats() {
        int[] seats = { 0, 7001, 7002 };
        Assert.Multiple(() => {
            Assert.That(RideShare.ClearSeats(seats), Is.EqualTo(new[] { 7001, 7002 }));
            Assert.That(seats, Is.EqualTo(new[] { 0, 0, 0 }));
            Assert.That(RideShare.ClearSeats(new int[0]), Is.Empty);
        });
    }

    [TestCase(RideOnType.Battle, true)]
    [TestCase(RideOnType.Object, true)]
    [TestCase(RideOnType.Default, false)]
    [TestCase(RideOnType.None, false)]
    public void DismountsOnDeath(RideOnType type, bool expected) {
        Assert.That(RideShare.DismountsOnDeath(type), Is.EqualTo(expected));
    }

    [TestCase(true, false, true)]
    [TestCase(true, true, false)]
    [TestCase(false, false, false)]
    public void DismountsOnEnter(bool riding, bool fieldAllowsRide, bool expected) {
        Assert.That(RideShare.DismountsOnEnter(riding, fieldAllowsRide), Is.EqualTo(expected));
    }

    [TestCase(true, 15L, 15)]
    [TestCase(false, 15L, 0)]
    [TestCase(true, 0L, 0)]
    public void DashStamina(bool owner, long runXStamina, int expected) {
        Assert.That(RideShare.DashStamina(owner, runXStamina), Is.EqualTo(expected));
    }

    [Test]
    public void Start_Allowed() {
        Assert.That(RideShare.CanStart(dead: false, fieldAllowsRide: true, riding: false), Is.True);
    }

    [Test]
    public void Start_DeadPlayer_IsRefused() {
        Assert.That(RideShare.CanStart(dead: true, fieldAllowsRide: true, riding: false), Is.False);
    }

    [Test]
    public void Start_FieldWithoutRides_IsRefused() {
        Assert.That(RideShare.CanStart(dead: false, fieldAllowsRide: false, riding: false), Is.False);
    }

    [Test]
    public void Start_AlreadyRiding_IsRefused() {
        Assert.That(RideShare.CanStart(dead: false, fieldAllowsRide: true, riding: true), Is.False);
    }

    [Test]
    public void Change_OwnerOfAMountItemRide() {
        Assert.That(RideShare.CanChange(owner: true, RideOnType.Default), Is.True);
    }

    [Test]
    public void Change_Passenger_IsRefused() {
        Assert.That(RideShare.CanChange(owner: false, RideOnType.Default), Is.False);
    }

    [Test]
    public void Change_BattleOrObjectMount_IsRefused() {
        Assert.That(RideShare.CanChange(owner: true, RideOnType.Battle), Is.False);
        Assert.That(RideShare.CanChange(owner: true, RideOnType.Object), Is.False);
    }
}
