using System.Numerics;

namespace Maple2.Model.Validators;

// RequestMoveField 0 (portal use): the client uses only a portal whose box holds the character (PortalAction::vfunc_1,
// actionkeyobject.cpp:860; WorldService::FUN_141bc2c90, worldservice_1.cpp:4231). The box (Unk_PortalBox, portal.cpp:112)
// is half the portal's size each way, turned by its Z rotation about its position, and its centre moved by the
// xblock's offset in world space; X and Y rotation are not read.
public static class PortalRules {
    // Whether the point is in the box grown by slack each way. A portal the server spawned has no offset; the client
    // takes its box from the packet, so a box centred on the position alone is tested too.
    public static bool InBox(Vector3 point, Vector3 position, Vector3 rotation, Vector3 dimension, Vector3 offset, float slack) {
        return InBox(point, position + offset, rotation.Z, dimension, slack)
            || (offset != Vector3.Zero && InBox(point, position, rotation.Z, dimension, slack));
    }

    private static bool InBox(Vector3 point, Vector3 center, float degrees, Vector3 dimension, float slack) {
        Vector3 local = point - center;
        float radians = -degrees * MathF.PI / 180f;
        float cos = MathF.Cos(radians), sin = MathF.Sin(radians);
        float x = local.X * cos - local.Y * sin;
        float y = local.X * sin + local.Y * cos;
        return MathF.Abs(x) <= MathF.Abs(dimension.X) / 2 + slack
            && MathF.Abs(y) <= MathF.Abs(dimension.Y) / 2 + slack
            && MathF.Abs(local.Z) <= MathF.Abs(dimension.Z) / 2 + slack;
    }
}
