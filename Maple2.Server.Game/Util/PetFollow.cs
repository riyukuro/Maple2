using System.Numerics;
using Maple2.Model.Metadata;

namespace Maple2.Server.Game.Util;

/// <summary>
/// How a summoned pet keeps up with its owner. The client sends no pet movement, so the server moves the pet.
/// The pet's petproperty row gives the distances: traceDistance (farther than this the pet follows) and
/// warpDistance (farther than this it is put next to the owner); 135 rows give 200 or 300 and 1200 or 3000.
/// The constants below are the fallback for a row that gives none; the stop distance and the warp height are
/// server choices (no table gives them). battleTraceDistance is not used: an owned pet does not fight here.
/// </summary>
public static class PetFollow {
    public enum Action { Stay, Walk, Warp }

    // Closer than this the pet stays where it is (a row's traceDistance, else this).
    public const float FollowDistance = 300f;
    // A walk ends this far from the owner, on the pet's side.
    public const float StopDistance = 150f;
    // Farther than this (a GM coordinate move, a fall, a lift) the pet is put next to the owner (a row's
    // warpDistance, else this).
    public const float WarpDistance = 1500f;
    public const float WarpHeight = 500f;

    /// <summary>The follow and warp distances of a pet row; a missing or bad value takes the fallback.</summary>
    public static (float Follow, float Warp) Distances(PetMetadataDistance? distance) {
        float follow = distance != null && float.IsFinite(distance.Trace) && distance.Trace > 0 ? distance.Trace : FollowDistance;
        float warp = distance != null && float.IsFinite(distance.Warp) && distance.Warp > 0 ? distance.Warp : WarpDistance;
        // A warp closer than the follow distance would warp a pet that should walk.
        return (follow, MathF.Max(warp, follow));
    }

    public static Action Decide(Vector3 pet, Vector3 owner, out Vector3 target) =>
        Decide(pet, owner, FollowDistance, WarpDistance, out target);

    public static Action Decide(Vector3 pet, Vector3 owner, PetMetadataDistance? distance, out Vector3 target) {
        (float follow, float warp) = Distances(distance);
        return Decide(pet, owner, follow, warp, out target);
    }

    private static Action Decide(Vector3 pet, Vector3 owner, float followDistance, float warpDistance, out Vector3 target) {
        target = pet;
        if (!IsFinite(pet) || !IsFinite(owner)) {
            target = owner;
            return IsFinite(owner) ? Action.Warp : Action.Stay;
        }

        Vector2 flat = new Vector2(pet.X - owner.X, pet.Y - owner.Y);
        float distance = flat.Length();
        if (distance > warpDistance || MathF.Abs(pet.Z - owner.Z) > WarpHeight) {
            target = owner;
            return Action.Warp;
        }

        if (distance <= followDistance) {
            return Action.Stay;
        }

        // The walk ends inside the follow distance, so it does not start again at once.
        Vector2 side = flat / distance * MathF.Min(StopDistance, followDistance / 2);
        target = new Vector3(owner.X + side.X, owner.Y + side.Y, owner.Z);
        return Action.Walk;
    }

    private static bool IsFinite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}
