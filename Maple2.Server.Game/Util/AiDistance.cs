using System.Numerics;

namespace Maple2.Server.Game.Util;

/// <summary>
/// The AI conditions distanceLess (27 in 6 AI files) and distanceOver (5 in 2) test the distance to the target. The
/// files settle which way: AI_DefaultPetTaming and AI_EventGoldPharaohMeowCat trace a target over 800 or 400 away and
/// run away from one less than 400 away, and AI_Primadonna's rush tests less than 300, 600, 900, ... 1800 in order in
/// one conditions node, which takes the first that passes, so each bracket is a nearer one first.
/// </summary>
public static class AiDistance {
    public static bool Less(Vector3 self, Vector3 target, int value) =>
        Vector3.DistanceSquared(self, target) < (float) value * value;

    public static bool Over(Vector3 self, Vector3 target, int value) =>
        Vector3.DistanceSquared(self, target) > (float) value * value;
}
