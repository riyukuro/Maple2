using System.Numerics;

namespace Maple2.Server.Game.Util;

/// <summary>
/// How a summoned pet keeps up with its owner. The client sends no pet movement, so the server moves the pet;
/// the client tables give no follow distances, so these are server choices.
/// </summary>
public static class PetFollow {
    public enum Action { Stay, Walk, Warp }

    // Closer than this the pet stays where it is.
    public const float FollowDistance = 300f;
    // A walk ends this far from the owner, on the pet's side.
    public const float StopDistance = 150f;
    // Farther than this (a GM coordinate move, a fall, a lift) the pet is put next to the owner.
    public const float WarpDistance = 1500f;
    public const float WarpHeight = 500f;

    public static Action Decide(Vector3 pet, Vector3 owner, out Vector3 target) {
        target = pet;
        if (!IsFinite(pet) || !IsFinite(owner)) {
            target = owner;
            return IsFinite(owner) ? Action.Warp : Action.Stay;
        }

        Vector2 flat = new Vector2(pet.X - owner.X, pet.Y - owner.Y);
        float distance = flat.Length();
        if (distance > WarpDistance || MathF.Abs(pet.Z - owner.Z) > WarpHeight) {
            target = owner;
            return Action.Warp;
        }

        if (distance <= FollowDistance) {
            return Action.Stay;
        }

        Vector2 side = flat / distance * StopDistance;
        target = new Vector3(owner.X + side.X, owner.Y + side.Y, owner.Z);
        return Action.Walk;
    }

    private static bool IsFinite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}
