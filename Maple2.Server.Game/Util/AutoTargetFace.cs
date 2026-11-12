using System.Numerics;

namespace Maple2.Server.Game.Util;

/// The skill autoTargeting check, as the client's PCBase::vfunc_101 makes it: the target must be inside a cylinder
/// around the caster (radius autoTargetingMaxDistance, height autoTargetingMaxHeight) and inside a cone of
/// autoTargetingMaxDegree degrees (the whole width, half on each side) around the caster's facing, on the ground plane.
public static class AutoTargetFace {
    /// The client's cylinder height when autoTargetingMaxHeight is 0.
    public const float DefaultHeight = 150f;

    public static bool Passes(Vector3 offset, Vector3 front, float maxDegree, float maxDistance, float maxHeight) {
        var flat = new Vector2(offset.X, offset.Y);
        float distance = flat.Length();
        if (!float.IsFinite(distance)) {
            return false;
        }

        // The client takes the skill's attack range for a radius of 0; that range is not kept here, so 0 sets no limit.
        if (maxDistance > 0 && distance > maxDistance) {
            return false;
        }

        float height = maxHeight != 0 ? maxHeight : DefaultHeight;
        if (Math.Abs(offset.Z) > height / 2) {
            return false;
        }

        var aim = new Vector2(front.X, front.Y);
        if (distance < 1e-3f || aim.LengthSquared() < 1e-6f) {
            return false;
        }

        float dot = Vector2.Dot(Vector2.Normalize(aim), flat / distance);
        return dot >= MathF.Cos(maxDegree / 2 * MathF.PI / 180);
    }
}
