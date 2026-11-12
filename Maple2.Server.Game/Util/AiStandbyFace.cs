using System.Numerics;

namespace Maple2.Server.Game.Util;

public static class AiStandbyFace {
    /// <summary>
    /// faceTarget="1" on a standby node turns the npc to its battle target when the standby starts. AI_Default has it on
    /// the standby after each skill, so the next skill starts facing the target (skills such as a lunge with motion
    /// faceTarget="0" do not turn the caster themselves).
    /// </summary>
    public static bool FacesTarget(int faceTarget) => faceTarget == 1;

    /// <summary>The unit direction from the npc to the target, or null when both stand on the same spot.</summary>
    public static Vector3? Direction(Vector3 from, Vector3 to) {
        Vector3 offset = to - from;
        float length = offset.Length();
        if (!float.IsFinite(length) || length < 1e-3f) {
            return null;
        }
        return offset / length;
    }
}
