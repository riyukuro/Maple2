using System.Numerics;
using Maple2.Model.Validators;

namespace Maple2.Server.Game.Util;

/// <summary>
/// How far from a drop a pickup may come. The client asks for a drop (RequestItemPickup 0x1C, RequestMesoPickup 0x1D)
/// only when its picker is within range of the drop as the client shows it (CDropItem::FUN_141190120 and
/// FUN_1411903c0, dropitem.cpp:789 and :842): the DropItemMaxLength constant for an item and DropMoneyMaxLength for
/// mesos (KR 300 each), or for an item the distance the drop was thrown if that is longer. The client throws each drop
/// from the server's position by a base plus a random distance (CDropItem::SetState, dropitem.cpp:430-540): an npc's
/// dropDistanceBase + dropDistanceRandom (KR rows at most 580 together), an interact object's dropDistance + 100 (at most 300),
/// the money tree 150 + 200, else 50 + 100. The drop then falls to the ground, so its height is the client's own.
/// </summary>
public static class PickupRules {
    // The client's fallback when the constant is not loaded (the KR row).
    public const float DefaultMaxLength = 300f;
    // Server rule: the farthest the client throws a drop from the server's position (KR at most 580).
    public const float MaxThrow = 600f;

    /// <summary>The horizontal reach of a pickup: the client's range (at least the throw) plus the throw from the
    /// server's position, plus the position lag.</summary>
    public static float Reach(int maxLength) {
        float range = maxLength > 0 ? maxLength : DefaultMaxLength;
        return MathF.Max(range, MaxThrow) + MaxThrow + InteractRules.ReachSlack;
    }

    /// <summary>Whether a picker at <paramref name="picker"/> can reach the drop at <paramref name="drop"/>, on the
    /// ground plane (the client's drop falls to a height the server does not know).</summary>
    public static bool InReach(Vector3 picker, Vector3 drop, float reach) {
        var flat = new Vector2(picker.X - drop.X, picker.Y - drop.Y);
        return float.IsFinite(flat.X) && float.IsFinite(flat.Y) && flat.Length() <= reach;
    }
}
