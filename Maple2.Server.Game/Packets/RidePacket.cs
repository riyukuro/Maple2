using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Game.Packets;

public static class RidePacket {
    private enum Command : byte {
        Start = 0,
        Stop = 1,
        Change = 2,
        Join = 3,
        Leave = 4,
        ChangeShared = 5,
        RideAlone = 6,
    }

    public static ByteWriter Start(Ride ride) {
        var pWriter = Packet.Of(SendOp.ResponseRide);
        pWriter.Write<Command>(Command.Start);
        pWriter.WriteInt(ride.OwnerId);
        pWriter.WriteClass<RideOnAction>(ride.Action);

        return pWriter;
    }

    public static ByteWriter Stop(int ownerId, RideOffAction action) {
        var pWriter = Packet.Of(SendOp.ResponseRide);
        pWriter.Write<Command>(Command.Stop);
        pWriter.WriteInt(ownerId);
        pWriter.WriteClass<RideOffAction>(action);

        return pWriter;
    }

    public static ByteWriter Change(int ownerId, int rideId, long itemUid) {
        var pWriter = Packet.Of(SendOp.ResponseRide);
        pWriter.Write<Command>(Command.Change);
        pWriter.WriteInt(ownerId);
        pWriter.WriteInt(rideId);
        pWriter.WriteLong(itemUid);

        return pWriter;
    }

    public static ByteWriter Join(int ownerId, int joinerId, sbyte index) {
        var pWriter = Packet.Of(SendOp.ResponseRide);
        pWriter.Write<Command>(Command.Join);
        pWriter.WriteInt(ownerId);
        pWriter.WriteInt(joinerId);
        pWriter.Write<sbyte>(index);

        return pWriter;
    }

    public static ByteWriter Leave(int ownerId, int leaverId) {
        var pWriter = Packet.Of(SendOp.ResponseRide);
        pWriter.Write<Command>(Command.Leave);
        pWriter.WriteInt(ownerId);
        pWriter.WriteInt(leaverId);

        return pWriter;
    }

    // The client reads: object id of the player, then the "ride alone" option byte.
    public static ByteWriter RideAlone(int objectId, bool alone) {
        var pWriter = Packet.Of(SendOp.ResponseRide);
        pWriter.WriteByte((byte) Command.RideAlone);
        pWriter.WriteInt(objectId);
        pWriter.WriteBool(alone);

        return pWriter;
    }

    // The client clears the owner's passengers (and resets a local passenger's camera).
    public static ByteWriter ChangeShared(int ownerId) {
        var pWriter = Packet.Of(SendOp.ResponseRide);
        pWriter.Write<Command>(Command.ChangeShared);
        pWriter.WriteInt(ownerId);

        return pWriter;
    }
}
