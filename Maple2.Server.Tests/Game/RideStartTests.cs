using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game;

// RequestRide mode 0 (Start) and mode 6 (ride alone). A start the field forbids, a mount that is not in the mount
// bag, one of another ride id, and an expired one each answer with a message box and no ride; a start of another
// ride type is dropped without an answer. The ride-alone option is told to the field even without a ride.
public class RideStartTests {
    private const int RideId = 21000021;
    private TestGameSession test = null!;
    private GameSession session => test.Session;

    private Item mount = null!;

    // The mount is stored before the session starts, as the bag is loaded at the start.
    private void Begin(bool fieldAllowsRide, long expiry = 0) {
        test = new TestGameSession();
        ItemMetadata data = TestGameSession.Metadata(50600001, type: 5);
        data = data with { Property = data.Property with { Ride = RideId } };
        mount = test.Store(data);
        test.Start();
        if (expiry != 0) {
            session.Item.Inventory.Get(mount.Uid, InventoryType.Mount)!.ExpiryTime = expiry;
        }
        MapMetadata map = TestGameSession.Map(2000001) with {
            Limit = new MapMetadataLimit(0, 0, 0, 0, [], true, true, true, true, true, fieldAllowsRide, true),
        };
        test.EnterMap(map);
        session.Ride = new RideManager(session);
        test.TakeSent();
    }

    private void Start(RideOnType type, int rideId, long itemUid) {
        new RideHandler { RideMetadata = null! }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(0);
            writer.WriteByte((byte) type);
            writer.WriteInt(rideId);
            writer.WriteInt(0);
            writer.WriteBool(false);
            writer.WriteInt(0);
            writer.WriteLong(itemUid);
        }));
    }

    [Test]
    public void AFieldThatForbidsRides_AnswersWithAMessageBox() {
        Begin(fieldAllowsRide: false);
        Start(RideOnType.Default, RideId, mount.Uid);
        Assert.That(session.Ride.Ride, Is.Null);
        Assert.That(test.TakeSent(), Has.Count.EqualTo(1));
    }

    [Test]
    public void AMountNotInTheBag_AnswersWithAMessageBox() {
        Begin(fieldAllowsRide: true);
        Start(RideOnType.Default, RideId, 987654321);
        Assert.That(session.Ride.Ride, Is.Null);
        Assert.That(test.TakeSent(), Has.Count.EqualTo(1));
    }

    [Test]
    public void AMountOfAnotherRideId_AnswersWithAMessageBox() {
        Begin(fieldAllowsRide: true);
        Start(RideOnType.Default, RideId + 1, mount.Uid);
        Assert.That(session.Ride.Ride, Is.Null);
        Assert.That(test.TakeSent(), Has.Count.EqualTo(1));
    }

    [Test]
    public void AnExpiredMount_AnswersWithAMessageBox() {
        Begin(fieldAllowsRide: true, expiry: 1);
        Start(RideOnType.Default, RideId, mount.Uid);
        Assert.That(session.Ride.Ride, Is.Null);
        Assert.That(test.TakeSent(), Has.Count.EqualTo(1));
    }

    [Test]
    public void ABattleRideType_IsDroppedSilently() {
        Begin(fieldAllowsRide: true);
        Start(RideOnType.Battle, RideId, mount.Uid);
        Assert.That(session.Ride.Ride, Is.Null);
        Assert.That(test.TakeSent(), Is.Empty);
    }

    [Test]
    public void RideAlone_IsToldToTheFieldWithoutARide() {
        Begin(fieldAllowsRide: true);
        new RideHandler { RideMetadata = null! }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(6);
            writer.WriteBool(true);
        }));
        Assert.That(test.TakeSent(), Has.Count.EqualTo(1));
    }
}
