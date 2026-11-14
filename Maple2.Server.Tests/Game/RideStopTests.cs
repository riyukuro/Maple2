using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game;

// RequestRide mode 1 (Stop). The client's MyPC::SendRequestRideMode1 sends it for a plain dismount (the quickslot
// toggle of the mount item, UseRideeItem) as 01, then the ride-off action: type 0 (Default) and the forced byte 0.
// The client gets off only on the server's ResponseRide mode 1 for its own object id.
public class RideStopTests {
    private TestGameSession test = null!;

    private GameSession session => test.Session;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.Start();
        test.EnterMap(TestGameSession.Map(2000001));
        session.Ride = new RideManager(session);
        // The key table the end of a ride sends again (the fixture's config is not loaded).
        const BindingFlags any = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(ConfigManager).GetField("session", any)!.SetValue(session.Config, session);
        typeof(ConfigManager).GetField("keyBinds", any)!.SetValue(session.Config, new Dictionary<int, KeyBind>());
        test.TakeSent();
    }

    private static readonly RideMetadata Duck = new(21000021, "", new RideMetadataBasic(RideOnType.Default, 0, 0, 0, false, 0, 0),
        new RideMetadataSpeed(0, 0, 0, 0), new Dictionary<BasicAttribute, long>());

    // What the client sends for the quickslot toggle while riding: Stop, ride-off type Default, not forced.
    private void SendStop() {
        new RideHandler { RideMetadata = null! }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(1);
            writer.WriteByte(0);
            writer.WriteBool(false);
        }));
    }

    private List<byte[]> SentStops() => test.TakeSent()
        .Where(packet => TestGameSession.Op(packet) == SendOp.ResponseRide && packet[2] == 1).ToList();

    private byte[] Stop(int objectId) => [(byte) SendOp.ResponseRide, (byte) ((int) SendOp.ResponseRide >> 8), 1,
        (byte) objectId, (byte) (objectId >> 8), (byte) (objectId >> 16), (byte) (objectId >> 24), 0, 0];

    [Test]
    public void StopEndsTheRideAndAnswersTheRider() {
        Item item = test.Store(TestGameSession.Metadata(50600001));
        session.Ride.Ride = new Ride(session.Player.ObjectId, Duck, new RideOnActionDefault(Duck.Id, 1, item));

        SendStop();

        Assert.That(session.Ride.Ride, Is.Null);
        Assert.That(SentStops(), Is.EqualTo(new[] { Stop(session.Player.ObjectId) }));
    }

    // The server has no ride for the player but the client still rides (a lost Stop, or a ride the server ended
    // without the client getting off): today a desync is permanent, since every later Stop was dropped and the
    // client never sends another Start while it rides. The requester gets its Stop back (chosen here).
    [Test]
    public void StopWithoutAServerRideStillGetsTheRiderOff() {
        SendStop();

        Assert.That(session.Ride.Ride, Is.Null);
        Assert.That(SentStops(), Is.EqualTo(new[] { Stop(session.Player.ObjectId) }));
    }
}
