using System.Numerics;
using Maple2.Model.Enum;

namespace Maple2.Server.Game.Util;

/// <summary>
/// The AI jump node (1838 in 192 AI files; every one gives pos, speed and heightMultiplier). The client moves the npc:
/// CNpc::ApplySyncBlock reads sequence -2 (Jump_A) or -3 (Jump_B) and then the jump block. The absolute form gives
/// from, to, a duration in seconds and a height scale. The arc runs (1-t)*from + t*to, with z raised by
/// 4*t*(1-t)*height, where height = 150/212.132 * |to-from| * scale. The client does not place the npc while the
/// arc runs, and state 6 (StateJumpNpc) plays the Jump_A / Jump_B animation. The node's pos is a world point (the
/// files give the ship deck or the arena points). Only type="2" (10 nodes) asks for Jump_B; an absent type is jumpA.
/// The client has no speed: the duration (distance / speed, in units a second) is the server's reading of it.
/// </summary>
public static class AiJump {
    public const short SequenceJumpA = -2;
    public const short SequenceJumpB = -3;
    // The client's own duration for the relative block (Unk_NpcBeam::Set): the fallback for a bad speed.
    public const float DefaultDuration = 0.45f;

    public static short Sequence(NodeJumpType type) => type == NodeJumpType.JumpB ? SequenceJumpB : SequenceJumpA;

    public static float Duration(Vector3 from, Vector3 to, int speed) {
        if (speed <= 0) {
            return DefaultDuration;
        }
        float duration = Vector3.Distance(from, to) / speed;
        return float.IsFinite(duration) && duration > 0 ? duration : DefaultDuration;
    }

    /// <summary>The top of the arc above the straight line, as the client draws it.</summary>
    public static float Height(Vector3 from, Vector3 to, float scale) => 150f / 212.132f * Vector3.Distance(from, to) * scale;

    /// <summary>
    /// The npc's place on the arc at t (0 at the take-off, 1 at the landing), as the client flies it: the straight
    /// line with z raised by 4*t*(1-t)*height.
    /// </summary>
    public static Vector3 Point(Vector3 from, Vector3 to, float scale, float t) {
        t = Math.Clamp(t, 0f, 1f);
        Vector3 point = Vector3.Lerp(from, to, t);
        point.Z += 4 * t * (1 - t) * Height(from, to, scale);
        return point;
    }
}
