using System.Collections.Generic;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game;

// The ride manager's own paths: swapping the mount item (mode 2) ends the old ride and tells the field, entering a
// field that forbids rides ends the ride with the rider alone told, and a leave keeps an owner's ride but ends a
// passenger's seat.
public class RideManagerTests {
    private TestGameSession test = null!;
    private GameSession session => test.Session;

    private static readonly RideMetadata Duck = new(21000021, "", new RideMetadataBasic(RideOnType.Default, 0, 0, 0, false, 0, 2),
        new RideMetadataSpeed(0, 0, 0, 0), new Dictionary<BasicAttribute, long>());

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.Start();
        test.EnterMap(TestGameSession.Map(2000001));
        session.Ride = new RideManager(session);
        const BindingFlags any = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(ConfigManager).GetField("session", any)!.SetValue(session.Config, session);
        typeof(ConfigManager).GetField("keyBinds", any)!.SetValue(session.Config, new Dictionary<int, KeyBind>());
        test.TakeSent();
    }

    private void Ride(int ownerId) {
        Item item = test.Store(TestGameSession.Metadata(50600001));
        session.Ride.Ride = new Ride(ownerId, Duck, new RideOnActionDefault(Duck.Id, 1, item));
    }

    [Test]
    public void Change_WithoutARide_IsRefused() {
        Assert.That(session.Ride.Change(Duck.Id, 1), Is.False);
        Assert.That(test.TakeSent(), Is.Empty);
    }

    [Test]
    public void Change_ByAPassenger_IsRefused() {
        Ride(session.Player.ObjectId + 1);
        Assert.That(session.Ride.Change(Duck.Id, 1), Is.False);
        Assert.That(session.Ride.Ride, Is.Not.Null);
    }

    [Test]
    public void Change_ByTheOwner_EndsTheRideAndTellsTheField() {
        Ride(session.Player.ObjectId);
        Assert.That(session.Ride.Change(Duck.Id, 1), Is.True);
        Assert.That(session.Ride.Ride, Is.Null);
        Assert.That(test.TakeSent(), Is.Not.Empty);
    }

    [Test]
    public void LeavingTheField_KeepsAnOwnersRide() {
        Ride(session.Player.ObjectId);
        session.Ride.LeaveField(session.Field!);
        Assert.That(session.Ride.Ride, Is.Not.Null);
    }

    [Test]
    public void LeavingTheField_FreesAPassengersSeat() {
        Ride(session.Player.ObjectId + 1);
        Ride ride = session.Ride.Ride!;
        ride.Passengers[0] = session.Player.ObjectId;
        session.Ride.LeaveField(session.Field!);
        Assert.That(session.Ride.Ride, Is.Null);
        Assert.That(ride.Passengers[0], Is.Zero);
        Assert.That(test.TakeSent(), Is.Not.Empty);
    }

    [Test]
    public void EnteringAFieldThatForbidsRides_EndsTheRideForTheRiderAlone() {
        Ride(session.Player.ObjectId);
        MapMetadata map = TestGameSession.Map(2000002) with {
            Limit = new MapMetadataLimit(0, 0, 0, 0, [], true, true, true, true, true, false, true),
        };
        test.EnterMap(map);
        session.Ride.Ride = null;
        Ride(session.Player.ObjectId);
        test.TakeSent();

        session.Ride.EnterField(session.Field!);
        Assert.That(session.Ride.Ride, Is.Null);
        Assert.That(test.TakeSent(), Is.Not.Empty);
    }

    [Test]
    public void DashStamina_WithoutARide_TakesNothing() {
        session.Ride.ConsumeDashStamina();
        Assert.That(test.TakeSent(), Is.Empty);
    }
}
