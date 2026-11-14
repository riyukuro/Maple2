using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// RequestRide 3 (join): the client boards only a mount whose hold node is within FindHoldTargetRange (KR 230) of the
// joiner (HoldPCAction, actionkeyobject.cpp:2320). The server seated a joiner anywhere in the field on the mount,
// which moves the joiner there. The mount is made up.
public class RideJoinReachTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static (TestGameSession Owner, TestGameSession Joiner) Start(float x) {
        var a = new TestGameSession();
        a.Start();
        var b = new TestGameSession(a.Options);
        b.Start();
        FieldManager field = a.Session.Field!;
        typeof(Actor<Player>).GetField("<ObjectId>k__BackingField", Any)!.SetValue(b.Session.Player, 2);
        field.Players[2] = b.Session.Player;
        b.Session.Field = field;
        typeof(Actor<Player>).GetField("<Field>k__BackingField", Any)!.SetValue(b.Session.Player, field);
        a.Session.Player.Position = Vector3.Zero;
        b.Session.Player.Position = new Vector3(x, 0, 0);
        a.Session.Ride = new RideManager(a.Session);
        b.Session.Ride = new RideManager(b.Session);
        var metadata = new RideMetadata(1, "", new RideMetadataBasic(RideOnType.Default, 0, 0, 0, false, 0, 1),
            new RideMetadataSpeed(0, 0, 0, 0), new Dictionary<BasicAttribute, long>());
        a.Session.Ride.Ride = new Ride(a.Session.Player.ObjectId, metadata, new RideOnAction(1, a.Session.Player.ObjectId));
        a.TakeSent();
        b.TakeSent();
        return (a, b);
    }

    private static void Join(TestGameSession joiner, int ownerId) {
        new RideHandler { RideMetadata = null! }.Handle(joiner.Session, TestGameSession.Packet(writer => {
            writer.WriteByte(3);
            writer.WriteInt(ownerId);
        }));
    }

    [Test]
    public void AJoinNextToTheMount_IsSeated() {
        (TestGameSession owner, TestGameSession joiner) = Start(200);
        Join(joiner, owner.Session.Player.ObjectId);
        Assert.Multiple(() => {
            Assert.That(joiner.Session.Ride.Ride, Is.SameAs(owner.Session.Ride.Ride));
            Assert.That(owner.Session.Ride.Ride!.Passengers, Is.EqualTo(new[] { 2 }));
        });
    }

    [Test]
    public void AJoinFromAcrossTheField_IsRefused() {
        (TestGameSession owner, TestGameSession joiner) = Start(5000);
        Join(joiner, owner.Session.Player.ObjectId);
        Assert.Multiple(() => {
            Assert.That(joiner.Session.Ride.Ride, Is.Null);
            Assert.That(owner.Session.Ride.Ride!.Passengers, Is.EqualTo(new[] { 0 }));
            Assert.That(owner.TakeSent(), Is.Empty);
        });
    }

    [Test]
    public void Reach_IsTheHoldRangeAndNodeAllowancePlusSlack() {
        float edge = RideShare.JoinReach + RideShare.HoldNodeAllowance + Maple2.Model.Validators.InteractRules.ReachSlack;
        Assert.Multiple(() => {
            Assert.That(RideShare.InJoinReach(Vector3.Zero, new Vector3(edge, 0, 0)), Is.True);
            Assert.That(RideShare.InJoinReach(Vector3.Zero, new Vector3(edge + 1, 0, 0)), Is.False);
        });
    }
}
