using Maple2.Model.Metadata;

namespace Maple2.Model.Game;

public class Ride {
    public readonly int OwnerId; // ObjectId of owner.
    public readonly RideMetadata Metadata;
    public readonly RideOnAction Action;
    public readonly int[] Passengers;
    // Environment.TickCount64 when the ride started (the pickup race grace of a disablePickUp ride).
    public long StartTick { get; set; } = Environment.TickCount64;

    public Ride(int ownerId, RideMetadata metadata, RideOnAction action) {
        OwnerId = ownerId;
        Metadata = metadata;
        Action = action;
        Passengers = new int[metadata.Basic.Passengers];
    }
}
