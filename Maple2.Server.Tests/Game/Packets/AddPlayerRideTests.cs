using System.Collections.Generic;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Packets;
using Maple2.Tools.Extensions;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game.Packets;

// The ride block of FieldAddUser as CSceneStateGame::OnAddUser (scenestategame.cpp:11200) reads it: bool riding, the
// RideOnAction, then u8 n and n x (i32 rider object id, u8 seat).
public class AddPlayerRideTests {
    private const int OWNER = 100;

    private static Ride Ride(int seats) {
        var metadata = new RideMetadata(99910241, "", new RideMetadataBasic(RideOnType.Object, 0, 0, 0, false, 0, seats),
            new RideMetadataSpeed(1, 1, 1, 1), new Dictionary<BasicAttribute, long>());
        return new Ride(OWNER, metadata, new RideOnActionObject(metadata.Id, OWNER));
    }

    private static ByteReader Written(int objectId, Ride? ride) {
        var writer = new ByteWriter();
        FieldPacket.WriteRide(writer, objectId, ride);
        return new ByteReader(writer.Buffer[..writer.Length], 0);
    }

    private static int ActionLength(Ride ride) {
        var writer = new ByteWriter();
        writer.WriteClass<RideOnAction>(ride.Action);
        return writer.Length;
    }

    [Test]
    public void NotRiding_WritesFalseOnly() {
        ByteReader reader = Written(OWNER, null);
        Assert.That(reader.ReadBool(), Is.False);
        Assert.That(reader.Available, Is.Zero);
    }

    // A player who comes into view of a shared mount sees its passengers in their seats.
    [Test]
    public void Owner_WritesTheSeatedPassengers() {
        Ride ride = Ride(3);
        ride.Passengers[0] = 201;
        ride.Passengers[2] = 203;
        ByteReader reader = Written(OWNER, ride);

        Assert.That(reader.ReadBool(), Is.True);
        reader.Skip(ActionLength(ride));
        Assert.That(reader.ReadByte(), Is.EqualTo(2));
        Assert.That(reader.ReadInt(), Is.EqualTo(201));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(203));
        Assert.That(reader.ReadByte(), Is.EqualTo(2));
        Assert.That(reader.Available, Is.Zero);
    }

    [Test]
    public void OwnerAlone_WritesNoPassenger() {
        Ride ride = Ride(0);
        ByteReader reader = Written(OWNER, ride);

        Assert.That(reader.ReadBool(), Is.True);
        reader.Skip(ActionLength(ride));
        Assert.That(reader.ReadByte(), Is.Zero);
        Assert.That(reader.Available, Is.Zero);
    }

    // A passenger holds the owner's Ride; it is drawn on the owner's seat, not on a mount of its own.
    [Test]
    public void Passenger_WritesNoMount() {
        Ride ride = Ride(1);
        ride.Passengers[0] = 201;
        ByteReader reader = Written(201, ride);

        Assert.That(reader.ReadBool(), Is.False);
        Assert.That(reader.Available, Is.Zero);
    }
}
