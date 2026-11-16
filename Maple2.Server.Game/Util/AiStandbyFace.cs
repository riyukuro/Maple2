using System.Numerics;

namespace Maple2.Server.Game.Util;

public static class AiStandbyFace {
    /// <summary>
    /// faceTarget="1" on a standby node turns the npc to its battle target when the standby starts. AI_Default has it on
    /// the standby after each skill, so the next skill starts facing the target (skills such as a lunge with motion
    /// faceTarget="0" do not turn the caster themselves).
    /// faceTarget="3" (KR: 11 standby and 107 skill nodes, the skill ones mostly with a faceTargetTick) faces the target
    /// too; whatever more it means over the faceTargetTick (its values mix 0.9, 2.0 and 1200, so no unit is sure) is not
    /// done here, but it starts facing the target.
    /// </summary>
    public static bool FacesTarget(int faceTarget) => faceTarget is 1 or 3;

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
